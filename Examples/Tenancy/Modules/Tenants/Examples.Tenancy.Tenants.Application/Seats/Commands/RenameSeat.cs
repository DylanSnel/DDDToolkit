using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Commands;

/// <summary>
/// Gives a seat of the caller's tenant another name to be shown by: this application's own use case, about this
/// application's own field, under this application's own rule.
/// </summary>
/// <remarks>
/// Tenancy keeps no name of a seat, so it has no use case that renames one: the name is a field of the module's
/// <c>Seat</c> class, and what guards it is the module's to say. Its rule: <b>a seat renames itself, and another
/// seat takes <see cref="RequiredKey"/> for the whole tenant</b>. The request requires a caller who works in the
/// tenant, so nobody is refused before the handler; the handler asks the rest of the rule itself, as only it knows
/// which seat is renamed. System work in the tenant holds every key there.
/// <para>
/// On Postgres the database holds the name to the same rule. Tenancy's policy on the seats lets more callers change
/// the row, a seat that manages seats or grants anywhere in the tenant, since every save of a seat writes its version;
/// the package guards its own columns of the row and decides nothing about this module's. So the module's column rule,
/// <c>NameChangesByTheSeatOrWithTheSeatsKey</c> beside its infrastructure, holds the name to this rule for a
/// statement that goes round the handler, and a save it refuses is <c>access.refused</c>.
/// </para>
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="DisplayName">The name it is shown by from now on.</param>
public sealed record RenameSeat(SeatId Seat, string? DisplayName) : ICommand, ITenantsRequest
{
    /// <summary>The key a caller holds for the whole tenant to rename a seat that is not its own.</summary>
    public const string RequiredKey = TenancyKeys.SeatsManage;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>
/// Handles <see cref="RenameSeat"/>: asks the module's rule, loads the seat through the package's store, renames it
/// and saves it in the store's unit of work.
/// </summary>
/// <param name="store">The unit of work Tenancy's aggregates are loaded and saved in.</param>
/// <param name="answers">Tenancy's answers about the current caller.</param>
/// <param name="reads">Where the key is asked about: Tenancy's rows, on a context of the question's own.</param>
public sealed class RenameSeatHandler(TenancyUseCases.IStore store, SampleAnswers answers, ITenancyReads reads) : ICommandHandler<RenameSeat>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> for another seat without <see cref="RenameSeat.RequiredKey"/> for the whole
    /// tenant, <c>tenancy.seat-not-found</c> for a seat that is no seat of the tenant, and
    /// <c>tenants.seat.display-name</c> for a name the seat refuses.
    /// </exception>
    public async ValueTask<Unit> Handle(RenameSeat command, CancellationToken cancellationToken)
    {
        var caller = answers.Caller;
        var own = caller.Kind == TenancyCallerKind.Seat && caller.Seat is { } mine && mine.Equals(command.Seat);
        if (!own && !await TenantWideKey.IsHeldAsync(answers, reads, RenameSeat.RequiredKey, cancellationToken))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.NotPermitted, ("Key", RenameSeat.RequiredKey), ("Unit", null));
        }

        // The store keeps to the caller's tenant: a seat of another tenant is not found, as one of nobody is not.
        var seat = await store.FindSeatAsync(command.Seat, cancellationToken) ?? throw TenancyRefusals.Refuse(TenancyRefusals.SeatNotFound);
        seat.Rename(command.DisplayName);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
