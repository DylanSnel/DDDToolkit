using Examples.Tenancy.Tenants.Application.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Directory.Queries;

/// <summary>
/// What the seats with these ids are called, by name: each seat's id, the name this application keeps on it and its
/// status, never an identity. What a screen asks once another module answered it seat ids.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the package's directory answers such a caller about any seat
/// of that tenant. An id of another tenant, or of no seat, is left out of the answer without a word; more ids than
/// one question takes are refused.
/// </remarks>
/// <param name="Ids">The seats asked about.</param>
public sealed record SeatsById(IReadOnlyList<SeatId> Ids) : IQuery<IReadOnlyList<SeatListing>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>
/// Answers <see cref="SeatsById"/> from the Tenancy package's directory: the module's own seats, each selected with
/// the name its class keeps, by name.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class SeatsByIdHandler(ITenancyReads reads) : IQueryHandler<SeatsById, IReadOnlyList<SeatListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody, or <c>tenancy.too-many-ids</c>.
    /// </exception>
    public async ValueTask<IReadOnlyList<SeatListing>> Handle(SeatsById query, CancellationToken cancellationToken)
    {
        var seats = await reads.AskDirectoryAsync(directory => directory.SeatsByIdAsync(query.Ids, cancellationToken));
        return SeatListing.ByName(seats.Select(seat => new SeatListing(seat.Id, seat.DisplayName, seat.Status)));
    }
}
