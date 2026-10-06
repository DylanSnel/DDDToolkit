using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Application.Invitations.Commands;
using Examples.Tenancy.Tenants.Application.Invitations.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Invitations.Rest;

/// <summary>
/// Invitations over HTTP: inviting a person by address, the open invitations, revoking one, and accepting one.
/// Each route sends one command or query of the application project's <c>Invitations</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// groups the host hands it.
/// <para>
/// An invitation's token travels in a body, both ways, and in nothing else: no route has it in its path or its
/// query, where a server's log would keep it.
/// </para>
/// </remarks>
internal static class InvitationsEndpoints
{
    /// <summary>
    /// Maps <c>GET /tenancy/invitations</c>, <c>POST /tenancy/invitations</c> and
    /// <c>DELETE /tenancy/invitations/{invitationId}</c>, each for a seat in the tenant the request selected.
    /// </summary>
    public static IEndpointRouteBuilder MapInvitationsEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The invitations that can still be accepted, into the units where the caller manages seats: the address
        // each is for, and what it offers by id. Never a token.
        group.MapGet("/tenancy/invitations", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new OpenInvitations(), cancellationToken)).Select(Describe)));

        // The one answer that carries the token, which nothing keeps: not this API, and no cache on the way.
        // It says nothing of whether the address had an account.
        group.MapPost("/tenancy/invitations", async (PersonToInvite body, ISender sender, HttpContext http, CancellationToken cancellationToken) =>
        {
            var issued = await sender.Send(new InvitePerson(body.Address, body.UnitId, body.RoleId, body.EndsAt, body.DisplayName), cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { invitationId = issued.Id, token = issued.Token, expiresAt = issued.ExpiresAt });
        });

        group.MapDelete("/tenancy/invitations/{invitationId}", async (InvitationId invitationId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new CancelInvitation(invitationId), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// <c>POST /invitations/accept</c>: the signed-in person accepts the invitation their token is for, and is
    /// answered the seat they now have; <c>GET /me/seats</c> then lists it with its tenant. It needs a token and
    /// no tenant, so it is mapped outside the group that requires a seat.
    /// </summary>
    public static IEndpointRouteBuilder MapInvitationAcceptance(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/invitations/accept", async (InvitationToAccept body, ISender sender, CancellationToken cancellationToken) =>
        {
            var seat = await sender.Send(new AcceptInvitation(body.Token, body.DisplayName), cancellationToken);
            return Results.Ok(new { seatId = seat });
        });

        return group;
    }

    /// <summary>An open invitation as this project writes it: who it is for, and what it offers and who issued it, by id.</summary>
    private static object Describe(TenantsTenancy.OpenInvitation<InvitationId> invitation) => new
    {
        invitation.Id,
        invitation.Address,
        invitation.DisplayName,
        invitation.UnitId,
        invitation.RoleId,
        until = invitation.GrantUntil,
        invitation.IssuedAt,
        invitation.ExpiresAt,
        invitation.IssuedBy,
    };

    /// <summary>
    /// A person to invite: where the invitation goes, and the seat it offers, placed in a unit with a role there
    /// until <paramref name="EndsAt"/> (<c>until</c>), or for good. The address, the unit and the role are
    /// required: left out, a 400 invalid-request says what is wrong.
    /// </summary>
    public sealed record PersonToInvite(
        [property: JsonRequired] string Address,
        [property: JsonRequired] OrganizationUnitId UnitId,
        [property: JsonRequired] RoleId RoleId,
        [property: JsonPropertyName("until")] DateTimeOffset? EndsAt,
        string? DisplayName);

    /// <summary>The invitation to accept, by its token, and the name the new seat is shown by when the invitation suggests none.</summary>
    public sealed record InvitationToAccept([property: JsonRequired] string Token, string? DisplayName)
    {
        /// <summary>Says nothing of the token: a record prints its members, and a token must not end up in a log.</summary>
        public override string ToString() => nameof(InvitationToAccept);
    }
}
