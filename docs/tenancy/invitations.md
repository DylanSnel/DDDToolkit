---
title: "Tenancy: invitations"
sidebar_label: Invitations
---

# Tenancy: invitations

How a person without an account yet is invited into a tenant. Off until you turn them on; the words are in the
[overview](overview.md).

## What an invitation is

A seat belongs to a verified identity, so somebody without an account cannot be given one. An invitation holds their
place: for an address, into a unit, with a role and optionally an end for that role's grant. Accepting makes the
seat, its primary placement and the grant, in one save.

## Turning them on

One class of your own and three calls:

```csharp
// Shop.Tenants.Contracts
[EntityId<Guid>] public readonly partial record struct InvitationId;

// Shop.Tenants
[InvitationAggregate<InvitationId>] public sealed partial class ShopInvitation;

// In the context, after AddTenancy: two tables, the invitations and their tokens' digests.
modelBuilder.AddTenancyInvitations<ShopInvitation, InvitationId>();

// In the registration, after AddTenancy.
services.AddTenancyInvitations<ShopInvitation, InvitationId, ShopTenancyContext>();

// In the outbox, next to AddTenancyDomainEvents.
outbox.AddTenancyInvitationEvents<InvitationId>();
```

A context that names its tables passes the same `TenancyTableNames` as to `AddTenancy`. A database in use needs a
migration and, on Postgres, a new export of the access files.

## Issuing and accepting

The token is returned once, by `IssueAsync`; sending it is yours. Accepting needs no tenant: the token names it.

```csharp
public sealed class InviteColleague(TenancyUseCases.InvitationCommands<ShopInvitation, InvitationId> invitations, IShopMail mail)
{
    public async Task HandleAsync(string address, OrganizationUnitId unit, RoleId role, CancellationToken cancellationToken)
    {
        var issued = await invitations.IssueAsync(
            address, unit, role, grantUntil: null, lifetime: null, cancellationToken);

        // In the fragment of a link the token reaches the page and no server's log.
        await mail.SendAsync(address, $"https://shop.example/join#{issued.Token}", cancellationToken);
    }
}

// What the page calls once the person has signed in.
app.MapPost("/invitations/accept", async (
    AcceptInvitation request,
    TenancyUseCases.InvitationCommands<ShopInvitation, InvitationId> invitations,
    CancellationToken cancellationToken) =>
{
    var accepted = await invitations.AcceptAsync(request.Token, verifiedAddress: null, cancellationToken,
        configure: seat => seat.Rename(request.DisplayName));
    return Results.Ok(new { tenant = accepted.Slug });
});
```

`AcceptAsync` answers the tenant, its slug for the next request, and the new seat. `ListOpenAsync` answers the open
invitations, soonest to end first; `CancelAsync` ends one
([`InvitationCommands`](reference/use-cases.md#tenancyusecasesinvitationcommandstinvitation-tinvitationid)).

## Who may issue one

What adding the seat and giving it the role would take:

- `tenancy.seats.manage` for the whole tenant, and `tenancy.grants.manage` at the unit;
- for a role that manages access, while containment is on, each of its access-managing permissions at the unit
  until at least the grant's end ([Who may give a role](roles-and-permissions.md#who-may-give-a-role));
- or system work in the tenant, held to no seat.

Seats holding `tenancy.seats.manage` at its unit list and cancel it, whoever issued it; to any other seat it is
`tenancy.invitation-not-found`.

## Asked again when used

An invitation never gives more than its issuer could give when it is used: accepting asks the issuer's rights
again, for the role as it is then. An issuer since suspended or no longer able to give it leaves it unusable:
`tenancy.invitation-unbacked`, which says nothing about the issuer. One issued by system work is not asked again. The
unit, role and tenant must still be active.

## Who accepts

- A signed-in person: a verified identity, a token role that [holds seats](postgres.md#how-the-tenant-reaches-a-policy),
  no anonymous sign-in (else `tenancy.identity-required`). An identity with a seat in the tenant already gets
  `tenancy.identity-has-seat`: nobody invites themself into a role.
- The rest runs as system work in the invitation's tenant for the issuing seat, which the grant keeps as its giver.
- `configure` sets your seat class's fields (a seat has no name in Tenancy) once the seat holds the role, before
  anything is saved. If it throws, nothing is accepted: the invitation stays open.

## The address finds nobody

The package sends nothing to it and looks nobody up by it; it is forgotten once the invitation is accepted or
cancelled. Pass the address your identity provider verified as `verifiedAddress` to refuse an invitation for another
(`tenancy.address-mismatch`); left `null`, whoever holds the token and is signed in accepts.

## It ends, and works once

| Option, in the `configure` of `AddTenancyInvitations` | Default |
|---|---|
| `DefaultLifetime`, when `IssueAsync` is given no `lifetime` | 7 days |
| `MinLifetime` | 10 minutes |
| `MaxLifetime` | 30 days |

- Lifetimes out of order are refused at registration. A grant it offers must end later than the invitation.
- Open or not is decided from the clock when used; nothing marks it.
- The same person sending the token again gets the seat made then, without `configure` running.
- Of two acceptances at once, one saves; the other fails with `ConcurrencyConflictException`, and sent again gets
  what the first made.

| Code | Kind | When |
|---|---|---|
| `tenancy.invitation-not-found` | Not found | No invitation has the token, or the text is no token |
| `tenancy.invitation-lapsed`, `-used`, `-cancelled`, `-state` | Conflict | Its time ran out; somebody else accepted it; it was cancelled; cancelling one no longer open |
| `tenancy.address-invalid`, `tenancy.invitation-lifetime`, `tenancy.invitation-grant-ends-first` | Invalid | At issue, with its [`Field`](names-and-languages.md#refusals-in-english-and-dutch) |

All codes: [Refusals](reference/refusals.md#tenancyrefusals).

## The token

- 32 random bytes as 43 link-safe characters, from `BearerTokens.New()` in the toolkit's core (yours to use too).
- Only its SHA-256 digest is stored. 256 random bits cannot be guessed, so no rate limit of its own; a digest cannot
  be reversed, so no salt.
- It is in no event, refusal or log of the package. Keep it out of query strings, which servers log.

## Events

`tenancy.invitation-issued`, `-cancelled` and `-accepted` say [who acted](operators-and-history.md#access-history)
and carry ids, never the address. Accepting also raises the seat's own (added, placed, role granted), which the
access history keeps.

## On Postgres

[The second lock](postgres.md), row level security (RLS), holds the same lines: seats that manage seats at its unit
read an invitation; a seat that could make the seat and the grant adds one; a seat only cancels one, and a trigger
keeps what it offers, for every role. No caller's role reads the digests, an operator's included:
`invitation_of_digest` answers for one, to Tenancy's own system work alone. The start-up checks cover both tables and
the function.
