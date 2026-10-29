using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// What is asked about seats. Like a route, a field decides nothing: it sends the query the route of the same
/// feature sends, takes nothing but the sender, and answers what the query answered. The types beside this class
/// say which fields the schema shows of those objects.
/// </summary>
internal static class SeatsQueries
{
    /// <summary>
    /// Who the calling seat is: the seat manager's overview of it, with its tenant, the seat itself, where it is placed
    /// and with which roles for which period, the path of each unit and the roles its grants name, and every permission
    /// it holds now with where that permission reaches.
    /// </summary>
    [Query]
    public static async Task<SeatManager.SeatOverview> GetOverviewOfMineAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new OverviewOfMine(), cancellationToken);

    /// <summary>
    /// The caller's own seats in every tenant. The one field that needs no seat and no <c>Tenant</c> header:
    /// picking a tenant comes before being in one.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<SeatInTenant<Seat>>> GetSeatsOfMineAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new SeatsOfMine(), cancellationToken);

    /// <summary>Every seat of the tenant, by the name this application keeps on it.</summary>
    [Query]
    public static async Task<IReadOnlyList<Seat>> GetSeatsAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new TenantSeats(), cancellationToken);
}
