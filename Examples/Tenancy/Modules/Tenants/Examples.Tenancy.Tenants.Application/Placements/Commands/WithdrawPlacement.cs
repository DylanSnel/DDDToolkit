using Mediator;

namespace Examples.Tenancy.Tenants.Application.Placements.Commands;

/// <summary>
/// Withdraws a seat from a unit, which takes away every role it holds there.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.seats.manage</c> at the unit, and for what taking
/// each of those roles away would need.
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="Unit">The unit it is placed in.</param>
public sealed record WithdrawPlacement(SeatId Seat, OrganizationUnitId Unit) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="WithdrawPlacement"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's use cases that change a seat.</param>
public sealed class WithdrawPlacementHandler(SampleTenancy.SeatCommands seats) : ICommandHandler<WithdrawPlacement>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(WithdrawPlacement command, CancellationToken cancellationToken)
    {
        await seats.WithdrawAsync(command.Seat, command.Unit, cancellationToken);
        return Unit.Value;
    }
}
