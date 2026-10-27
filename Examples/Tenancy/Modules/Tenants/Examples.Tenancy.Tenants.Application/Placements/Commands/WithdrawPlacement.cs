using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Placements.Commands;

/// <summary>
/// Withdraws a seat from a unit, which takes away every role it holds there.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.seats.manage</c> at the unit, which the request names. The package's manager asks for
/// that permission again, and for what taking each of those roles away would need.
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="Unit">The unit it is placed in.</param>
[RequiresPermission(TenancyPermissions.SeatsManage)]
public sealed record WithdrawPlacement(SeatId Seat, [AtUnit] OrganizationUnitId Unit) : ICommand<Seat>, ITenantsRequest;

/// <summary>Handles <see cref="WithdrawPlacement"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's seat manager.</param>
public sealed class WithdrawPlacementHandler(SeatManager seats) : ICommandHandler<WithdrawPlacement, Seat>
{
    /// <inheritdoc />
    /// <returns>The seat, without the placement and its grants.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<Seat> Handle(WithdrawPlacement command, CancellationToken cancellationToken)
        => await seats.WithdrawAsync(command.Seat, command.Unit, cancellationToken);
}
