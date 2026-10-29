using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Commands;

/// <summary>
/// Suspends a seat: its placements and grants stay, and give it nothing until it is reactivated.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.seats.manage</c> for the whole tenant. The package's manager asks for that permission again, and
/// for what taking each of the seat's roles away would need; and it keeps the tenant an administrator.
/// </remarks>
/// <param name="Seat">The seat.</param>
[RequiresPermission(TenancyPermissions.SeatsManage)]
public sealed record SuspendTenantSeat(SeatId Seat) : ICommand<Seat>, ITenantsRequest;

/// <summary>Handles <see cref="SuspendTenantSeat"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's seat manager.</param>
public sealed class SuspendTenantSeatHandler(SeatManager seats) : ICommandHandler<SuspendTenantSeat, Seat>
{
    /// <inheritdoc />
    /// <returns>The seat, suspended.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<Seat> Handle(SuspendTenantSeat command, CancellationToken cancellationToken)
        => await seats.SuspendAsync(command.Seat, cancellationToken);
}
