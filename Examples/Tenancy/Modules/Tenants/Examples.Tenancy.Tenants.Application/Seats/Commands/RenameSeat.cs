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
/// seat takes <see cref="RequiredPermission"/> for the whole tenant</b>. Yours, or the permission: the request says so with
/// <c>[RequiresPermission(RequiredPermission)]</c> and <c>[OrOwn]</c> on the seat, which is
/// <c>AccessRequirement.Any(AccessRequirement.Own(Seat), TenancyAccess.AtTenant(RequiredPermission))</c>, and the check
/// holds the caller to it at the door: the seat it names is the caller's own seat in the tenant, or the caller holds the
/// permission. A caller for whom neither holds is refused with one <c>access.refused</c> that carries both reasons,
/// <c>access.not-own</c> and <c>tenancy.not-permitted</c> with the permission. System work in the tenant holds every permission there,
/// and has no seat of its own.
/// <para>
/// On Postgres the database holds the name to the same rule. Tenancy's policy on the seats lets more callers change
/// the row, a seat that manages seats or grants anywhere in the tenant, since every save of a seat writes its version;
/// the package guards its own columns of the row and decides nothing about this module's. So the module's column rule,
/// <c>NameChangesByTheSeatOrWithTheSeatsPermission</c> beside its infrastructure, holds the name to this rule for a
/// statement that goes round the handler, with the same two branches, and a save it refuses is <c>access.refused</c>.
/// </para>
/// </remarks>
/// <param name="Seat">The seat: the caller's own, or any of the tenant's with the permission.</param>
/// <param name="DisplayName">The name it is shown by from now on.</param>
[RequiresPermission(RequiredPermission)]
public sealed record RenameSeat([OrOwn] SeatId Seat, string? DisplayName) : ICommand, ITenantsRequest
{
    /// <summary>The permission a caller holds for the whole tenant to rename a seat that is not its own.</summary>
    public const string RequiredPermission = TenancyPermissions.SeatsManage;
}

/// <summary>
/// Handles <see cref="RenameSeat"/>: loads the seat through the package's store, renames it and saves it in the store's
/// unit of work. Who may rename which seat its requirement said at the door.
/// </summary>
/// <param name="store">The unit of work Tenancy's aggregates are loaded and saved in.</param>
public sealed class RenameSeatHandler(TenancyUseCases.IStore store) : ICommandHandler<RenameSeat>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.seat-not-found</c> for a seat that is no seat of the tenant, and <c>tenants.seat.display-name</c> for a
    /// name the seat refuses.
    /// </exception>
    public async ValueTask<Unit> Handle(RenameSeat command, CancellationToken cancellationToken)
    {
        // The store keeps to the caller's tenant: a seat of another tenant is not found, as one of nobody is not.
        var seat = await store.FindSeatAsync(command.Seat, cancellationToken) ?? throw TenancyRefusals.Refuse(TenancyRefusals.SeatNotFound);
        seat.Rename(command.DisplayName);
        await store.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
