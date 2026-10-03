using DDDToolkit.Abstractions.Access;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// The signed-in person's seats in every tenant, suspended ones included: what a tenant picker shows before the
/// person is in any tenant.
/// </summary>
/// <remarks>
/// Open, because it is asked before any tenant: there is no seat yet to hold a key, and no tenant to be in. What
/// keeps it safe is what it looks up, which the handler checks: the verified identity of the caller's own token
/// and nothing a request could supply. Which signed-in users hold seats at all is the Tenancy package's rule, and
/// the package applies it: a user whose token role holds no seat is answered no seats, whatever their identity has.
/// </remarks>
public sealed record SeatsOfMine : IQuery<IReadOnlyList<SeatOfCaller<TenantId, SeatId>>>, ITenantsRequest
{
    /// <summary>Why the query requires nothing of its caller.</summary>
    public const string OpenBecause =
        "A person's own seats are asked for before any tenant is chosen; they are looked up by the verified identity of the caller's token alone.";

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new AccessRequirement.Open(OpenBecause);
}

/// <summary>Answers <see cref="SeatsOfMine"/> from the Tenancy package, for the identity of the caller's token.</summary>
/// <param name="callers">Who is calling, as the host verified it.</param>
/// <param name="reads">Where Tenancy is read.</param>
public sealed class SeatsOfMineHandler(ICallerAccessor callers, ITenancyReads reads) : IQueryHandler<SeatsOfMine, IReadOnlyList<SeatOfCaller<TenantId, SeatId>>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-seated</c> for a caller that is not a signed-in user: anonymous, or the application's own work.
    /// </exception>
    public async ValueTask<IReadOnlyList<SeatOfCaller<TenantId, SeatId>>> Handle(SeatsOfMine query, CancellationToken cancellationToken)
    {
        // The one lookup by identity alone. The identity comes from the token, never from the request, and only
        // that verified identity is looked up: never an e-mail address.
        var caller = callers.Current;
        if (caller is not { Kind: CallerKind.User, UserId: not null })
        {
            throw TenancyRefusals.Of(TenancyRefusals.NotSeated);
        }

        return await reads.SeatsOfAsync(caller, cancellationToken);
    }
}
