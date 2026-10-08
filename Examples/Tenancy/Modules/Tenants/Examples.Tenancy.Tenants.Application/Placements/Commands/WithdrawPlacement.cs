using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Placements.Commands;

/// <summary>
/// Withdraws a seat from a unit, which takes away every role it holds there.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.seats.manage</c> at the unit, which the request names. The package's use case asks for
/// that key again, and for what taking each of those roles away would need.
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="Unit">The unit it is placed in.</param>
public sealed record WithdrawPlacement(SeatId Seat, OrganizationUnitId Unit) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(TenancyKeys.SeatsManage, Unit);
}

/// <summary>Handles <see cref="WithdrawPlacement"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's use cases that change a seat.</param>
public sealed class WithdrawPlacementHandler(TenancyUseCases.SeatCommands seats) : ICommandHandler<WithdrawPlacement>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(WithdrawPlacement command, CancellationToken cancellationToken)
    {
        await seats.WithdrawAsync(command.Seat, command.Unit, cancellationToken);
        return Unit.Value;
    }
}
