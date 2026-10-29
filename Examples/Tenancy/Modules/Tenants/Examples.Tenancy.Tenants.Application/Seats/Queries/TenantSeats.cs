using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// Every seat of the caller's tenant, by the name this application keeps on it, for the pickers.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, whom the seat manager answers. Each seat is the module's own class,
/// read for the question and tracked by nobody. It holds the seat's identity, so whatever shows a seat writes what it
/// chooses of it, and never the identity.
/// </remarks>
[InTenant]
public sealed record TenantSeats : IQuery<IReadOnlyList<Seat>>, ITenantsRequest;

/// <summary>Answers <see cref="TenantSeats"/> from the Tenancy package's seat manager, by name.</summary>
/// <param name="reads">Where Tenancy is read: the seat manager, in a scope of this query's own.</param>
public sealed class TenantSeatsHandler(ITenancyReads reads) : IQueryHandler<TenantSeats, IReadOnlyList<Seat>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">The caller's own refusal when it is nobody.</exception>
    public async ValueTask<IReadOnlyList<Seat>> Handle(TenantSeats query, CancellationToken cancellationToken)
        => SeatsByName.Ordered(await reads.AskAsync((SeatManager manager) => manager.ListAsync(cancellationToken)));
}
