using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Grants.Commands;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Grants.GraphQL;

/// <summary>
/// What gives a seat a role at a unit and takes it away. A mutation sends the command its route sends, then
/// answers the seat; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class GrantsMutations
{
    /// <summary>
    /// Gives a seat a role at a unit it is placed in, from now until <paramref name="until"/> or for good.
    /// A grant made by a client starts now: there is no argument for a start.
    /// </summary>
    [Mutation]
    public static async Task<TenantsTenancy.SeatSummary?> RoleGrantAsync(
        SeatId seatId,
        OrganizationUnitId unitId,
        RoleId roleId,
        DateTimeOffset? until,
        string? reason,
        [Service] ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new MakeGrant(seatId, unitId, roleId, until, reason), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }

    /// <summary>Takes a role a seat holds at a unit away.</summary>
    [Mutation]
    public static async Task<TenantsTenancy.SeatSummary?> RoleRevokeAsync(SeatId seatId, OrganizationUnitId unitId, RoleId roleId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new RevokeGrant(seatId, unitId, roleId), cancellationToken);
        return await sender.SeatNowAsync(seatId, cancellationToken);
    }
}
