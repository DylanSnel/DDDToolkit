using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// What is asked about seats. Like a route, a field decides nothing: it sends the query the route of the same
/// feature sends, takes nothing but the sender, and answers what the query answered. The types beside this class
/// say how the schema shows those records.
/// </summary>
internal static class SeatsQueries
{
    /// <summary>
    /// Who the calling seat is, where it is placed, which roles it holds and every key it holds now. This is
    /// Tenancy's own answer, so it carries names; where it names a unit or a role it uses small types of its own
    /// (<c>UnitPath</c>, <c>RoleOfMine</c>), never a half-filled entity.
    /// </summary>
    [Query]
    public static async Task<SampleTenancy.SeatOverview> GetOverviewOfMineAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new OverviewOfMine(), cancellationToken);

    /// <summary>
    /// The caller's own seats in every tenant. The one field that needs no seat and no <c>Tenant</c> header:
    /// picking a tenant comes before being in one.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<SeatOfCaller<TenantId, SeatId>>> GetSeatsOfMineAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new SeatsOfMine(), cancellationToken);

    /// <summary>Every seat of the tenant, by name.</summary>
    [Query]
    public static async Task<IReadOnlyList<SampleTenancy.SeatSummary>> GetSeatsAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new TenantSeats(), cancellationToken);
}
