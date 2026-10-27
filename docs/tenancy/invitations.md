---
title: "Tenancy: invitations"
sidebar_label: Invitations
---

# Tenancy: invitations

An invitation brings a person who has no account yet into a tenant. Invitations are off until you turn them on. The
words are defined in the [overview](overview.md).

## What an invitation is

A seat belongs to a verified identity, so a person without an account cannot get a seat. An invitation reserves the
place. It holds:

- the e-mail address it is for;
- the unit the seat will be placed at;
- the role the seat will get at that unit;
- optionally, an end for that role's grant.

Accepting the invitation creates the seat, its primary placement at the unit and the grant, in one save.

## Turning invitations on

You need one class of yours and three calls:

```csharp
// Shop.Tenants.Contracts
[EntityId<Guid>] public readonly partial record struct InvitationId;

// Shop.Tenants
[InvitationAggregate<InvitationId>] public sealed partial class ShopInvitation;

// In the context, after AddTenancy. Adds two tables: the invitations and the digests of their tokens.
modelBuilder.AddTenancyInvitations<ShopInvitation, InvitationId>();

// In the registration, after AddTenancy.
services.AddTenancyInvitations<ShopInvitation, InvitationId, ShopTenancyContext>();

// In the outbox, next to AddTenancyDomainEvents.
outbox.AddTenancyInvitationEvents<InvitationId>();
```

A context that names its tables passes the same `TenancyTableNames` as it passes to `AddTenancy`. A database in use
needs a migration. On Postgres it also needs a new export of the access files.

## Issuing and accepting

`IssueAsync` returns the token, once. Your code sends it to the person. Accepting needs no tenant, because the token
identifies the invitation and so its tenant.

```csharp
public sealed class InviteColleague(TenancyUseCases.InvitationCommands<ShopInvitation, InvitationId> invitations, IShopMail mail)
{
    public async Task HandleAsync(string address, OrganizationUnitId unit, RoleId role, CancellationToken cancellationToken)
    {
        var issued = await invitations.IssueAsync(
            address, unit, role, grantUntil: null, lifetime: null, cancellationToken);

        // The token goes in the link's fragment (after #). Browsers do not send the fragment, so no server log holds the token.
        await mail.SendAsync(address, $"https://shop.example/join#{issued.Token}", cancellationToken);
    }
}

// The page calls this route after the person signs in.
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

`AcceptAsync` returns the tenant, the tenant's slug for the next request, and the new seat. `ListOpenAsync` returns
the open invitations, the soonest to end first. `CancelAsync` cancels one invitation
([`InvitationCommands`](reference/use-cases.md#tenancyusecasesinvitationcommandstinvitation-tinvitationid)).

## Who may issue an invitation

Issuing requires what adding the seat and granting the role would require:

- The issuer needs `tenancy.seats.manage` for the whole tenant, and `tenancy.grants.manage` at the unit.
- For a role that manages access, while containment is on, the issuer needs each of the role's access-managing
  permissions at the unit, until at least the end of the grant
  ([Who may give a role](roles-and-permissions.md#who-may-give-a-role)).
- A role that manages no access needs nothing more. Its grant may last longer than the issuer's grants.
- System work in the tenant may issue any invitation, because no seat's rights limit system work.

A seat with `tenancy.seats.manage` at the invitation's unit lists and cancels the invitation, whoever issued it.
Every other seat gets `tenancy.invitation-not-found`.

## Checked again on acceptance

An invitation never gives more than its issuer could give at the moment it is accepted. Accepting checks the
issuer's rights again, against the role as it is then.

- When the issuer is suspended, or can no longer give the role, the acceptance fails with
  `tenancy.invitation-unbacked`. The refusal says nothing about the issuer.
- An invitation that system work issued is not checked again.
- The unit, the role and the tenant must still be active.

## Who accepts

- The person must be signed in, with a verified identity and a token role that
  [holds seats](postgres.md#passing-the-tenant-to-postgres). An anonymous sign-in does not count. Otherwise the
  call fails with `tenancy.identity-required`.
- An identity that already has a seat in the tenant gets `tenancy.identity-has-seat`. So nobody can invite
  themself into a role.
- The rest of the acceptance runs as system work in the invitation's tenant, for the issuing seat. The grant records
  that seat as its giver.
- `configure` sets the fields of your seat class. Tenancy stores no name for a seat, so this is where you set one.
  `configure` runs after the seat holds the role and before anything is saved. If it throws, nothing is accepted,
  and the invitation stays open.

## The e-mail address

- The package sends nothing to the address, and finds no person by it.
- The package forgets the address once the invitation is accepted or cancelled.
- Pass the address that your identity provider verified as `verifiedAddress`. Then an invitation for another
  address fails with `tenancy.address-mismatch`. The comparison ignores case.
- With `verifiedAddress: null`, anyone who holds the token and is signed in can accept.

## Lifetime and single use

| Option, in the `configure` of `AddTenancyInvitations` | Default |
|---|---|
| `DefaultLifetime`, when `IssueAsync` gets no `lifetime` | 7 days |
| `MinLifetime` | 10 minutes |
| `MaxLifetime` | 30 days |

- Registration refuses lifetimes out of order. `MinLifetime` must be more than zero and at most `DefaultLifetime`,
  and `DefaultLifetime` at most `MaxLifetime`.
- The grant an invitation offers must end later than the invitation.
- Each use compares the invitation's end with the clock. No job marks invitations as lapsed.
- When the same person sends the token again, the call returns the seat that the first acceptance made.
  `configure` does not run again.
- When two acceptances run at the same moment, one of them saves. The other fails with
  `ConcurrencyConflictException`. Sent again, it gets the result of the first acceptance: the seat, for the same
  person, or `tenancy.invitation-used`, for another person.

| Code | Kind | When |
|---|---|---|
| `tenancy.invitation-not-found` | Not found | No invitation has the token, or the text is not a token. |
| `tenancy.invitation-lapsed` | Conflict | The invitation's time ran out. |
| `tenancy.invitation-used` | Conflict | Somebody else accepted the invitation. |
| `tenancy.invitation-cancelled` | Conflict | The invitation was cancelled. |
| `tenancy.invitation-state` | Conflict | `CancelAsync` was called on an invitation that is no longer open. |
| `tenancy.address-invalid` | Invalid | `IssueAsync` got an address that is not one e-mail address of at most 254 characters. The `Field` argument is `address`. |
| `tenancy.invitation-lifetime` | Invalid | `IssueAsync` got a lifetime outside `MinLifetime` to `MaxLifetime`. The `Field` argument is `lifetime`. |
| `tenancy.invitation-grant-ends-first` | Invalid | `IssueAsync` got a grant that would end before the invitation. The `Field` argument is `grantUntil`. |

A form shows an invalid code's text under the input that the [`Field`](names-and-languages.md#refusals-in-english-and-dutch)
argument names. [TenancyRefusals](reference/refusals.md#tenancyrefusals) in the reference lists all codes.

## The token

- `BearerTokens.New()` in the toolkit's core makes the token: 32 random bytes, written as 43 URL-safe characters
  (base64url without padding). You can use `BearerTokens` for links of your own too.
- The store keeps only the token's SHA-256 digest. Nobody can guess 256 random bits, so the token needs no rate
  limit of its own. Nobody can reverse the digest, so the digest needs no salt.
- The token appears in no event, refusal or log of the package. Keep it out of query strings, because servers log
  them.

## Events

`tenancy.invitation-issued`, `tenancy.invitation-cancelled` and `tenancy.invitation-accepted` record
[who acted](operators-and-history.md#access-history) and carry ids. They never carry the address. The access history
does not keep them, because an invitation changes nobody's access until someone accepts it. Accepting also raises the
seat's events (seat added, seat placed, role granted), and the access history keeps those.

## Row level security on Postgres

On Postgres, [row level security](postgres.md) (RLS) applies the same rules in the database:

- A seat that manages seats at the invitation's unit reads the invitation. A read rule of yours on your invitation
  class can add readers within the tenant.
- A seat adds an invitation only when it could make the seat and the grant itself.
- A seat that manages seats at the invitation's unit can change an open invitation, and only to cancel it.
  Accepting runs as system work.
- A trigger keeps the offer fixed (unit, role and grant end), for every database role.
- No caller's role reads the token digests, an operator's role included. The database function
  `invitation_of_digest` returns the invitation for one digest, and only to Tenancy's system work.
- The start-up checks cover both tables and the function.
