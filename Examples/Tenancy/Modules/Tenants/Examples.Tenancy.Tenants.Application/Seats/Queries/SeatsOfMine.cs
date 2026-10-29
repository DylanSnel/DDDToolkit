using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// The signed-in person's seats in every tenant, suspended ones included: what a tenant picker shows before the
/// person is in any tenant. Each is the module's own seat, with the name its tenant keeps for the person, beside the
/// slug, the name and the status of that tenant.
/// </summary>
/// <remarks>
/// It requires a signed-in user and nothing more, because it is asked before any tenant: there is no seat yet to
/// hold a permission, and no tenant to be in. What keeps it safe is what it looks up: the verified identity of the
/// caller's own token and nothing a request could supply. Which signed-in users hold seats at all is the Tenancy
/// package's rule, and the package applies it: a user whose token role holds no seat is answered no seats,
/// whatever their identity has. Each seat holds the identity, so whatever shows a seat writes what it chooses of it,
/// and never the identity.
/// </remarks>
[SignedIn]
public sealed record SeatsOfMine : IQuery<IReadOnlyList<SeatInTenant<Seat>>>, ITenantsRequest;

/// <summary>
/// Answers <see cref="SeatsOfMine"/> from the Tenancy package, for the identity of the caller's token: the module's own
/// seats, each beside its tenant, as the package read them in one statement.
/// </summary>
/// <param name="user">Who is calling, as the host verified it.</param>
/// <param name="reads">Where Tenancy is read.</param>
public sealed class SeatsOfMineHandler(ICurrentUser user, ITenancyReads reads) : IQueryHandler<SeatsOfMine, IReadOnlyList<SeatInTenant<Seat>>>
{
    /// <inheritdoc />
    /// <exception cref="RefusalException">
    /// <c>access.not-signed-in</c> for a caller that is not a signed-in user: anonymous, or the application's own work.
    /// </exception>
    public async ValueTask<IReadOnlyList<SeatInTenant<Seat>>> Handle(SeatsOfMine query, CancellationToken cancellationToken)
    {
        // The one lookup by identity alone. The identity comes from the token, never from the request, and only
        // that verified identity is looked up: never an e-mail address. The request's requirement let only a
        // signed-in user this far; the lookup asks again, as it needs the identity, and refuses as it did.
        var current = user.Snapshot;
        if (current is not { Kind: CurrentUserKind.User, Id: not null })
        {
            throw ToolkitRefusals.Refuse(ToolkitRefusals.NotSignedIn);
        }

        return await reads.SeatsOfAsync(current, cancellationToken);
    }
}
