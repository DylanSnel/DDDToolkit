using Examples.Tenancy.Tenants.Application.Seats;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Directory.Queries;

/// <summary>
/// The seats with these ids, by the name this application keeps on each. What a screen asks once another module
/// answered it seat ids.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the seat manager answers such a caller about any seat of that
/// tenant. An id of another tenant, or of no seat, is left out of the answer without a word; more ids than one
/// question takes are refused. Each seat holds its identity, so whatever shows a seat writes what it chooses of it,
/// and never the identity.
/// </remarks>
/// <param name="Ids">The seats asked about.</param>
[InTenant]
public sealed record SeatsById(IReadOnlyList<SeatId> Ids) : IQuery<IReadOnlyList<Seat>>, ITenantsRequest;

/// <summary>Answers <see cref="SeatsById"/> from the Tenancy package's seat manager, by name.</summary>
/// <param name="reads">Where Tenancy is read: the seat manager, in a scope of this query's own.</param>
public sealed class SeatsByIdHandler(ITenancyReads reads) : IQueryHandler<SeatsById, IReadOnlyList<Seat>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody, or <c>tenancy.too-many-ids</c>.
    /// </exception>
    public async ValueTask<IReadOnlyList<Seat>> Handle(SeatsById query, CancellationToken cancellationToken)
        => SeatsByName.Ordered(await reads.AskAsync((SeatManager manager) => manager.ByIdAsync(query.Ids, cancellationToken)));
}
