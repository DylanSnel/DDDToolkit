using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// The signed-in person's seats in every tenant, suspended ones included: what a tenant picker shows before the
/// person is in any tenant, each seat with the name its tenant keeps for the person.
/// </summary>
/// <remarks>
/// It requires a signed-in user and nothing more, because it is asked before any tenant: there is no seat yet to
/// hold a key, and no tenant to be in. What keeps it safe is what it looks up: the verified identity of the
/// caller's own token and nothing a request could supply. Which signed-in users hold seats at all is the Tenancy
/// package's rule, and the package applies it: a user whose token role holds no seat is answered no seats,
/// whatever their identity has.
/// </remarks>
public sealed record SeatsOfMine : IQuery<IReadOnlyList<SeatOfMine>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.SignedIn();
}

/// <summary>
/// Answers <see cref="SeatsOfMine"/> from the Tenancy package, for the identity of the caller's token: the module's own
/// seats, each beside its tenant, selected into what the picker shows, with no read more.
/// </summary>
/// <param name="callers">Who is calling, as the host verified it.</param>
/// <param name="reads">Where Tenancy is read.</param>
public sealed class SeatsOfMineHandler(ICallerAccessor callers, ITenancyReads reads) : IQueryHandler<SeatsOfMine, IReadOnlyList<SeatOfMine>>
{
    /// <inheritdoc />
    /// <exception cref="RefusalException">
    /// <c>access.not-signed-in</c> for a caller that is not a signed-in user: anonymous, or the application's own work.
    /// </exception>
    public async ValueTask<IReadOnlyList<SeatOfMine>> Handle(SeatsOfMine query, CancellationToken cancellationToken)
    {
        // The one lookup by identity alone. The identity comes from the token, never from the request, and only
        // that verified identity is looked up: never an e-mail address. The request's requirement let only a
        // signed-in user this far; the lookup asks again, as it needs the identity, and refuses as it did.
        var caller = callers.Current;
        if (caller is not { Kind: CallerKind.User, UserId: not null })
        {
            throw ToolkitRefusals.Of(ToolkitRefusals.NotSignedIn);
        }

        var mine = await reads.SeatsOfAsync(caller, cancellationToken);
        return
        [
            .. mine.Select(found => new SeatOfMine(
                new TenantOfSeat(found.Seat.TenantId, found.Slug, found.OrganizationName, found.TenantStatus),
                new SeatListing(found.Seat.Id, found.Seat.DisplayName, found.Seat.Status))),
        ];
    }
}
