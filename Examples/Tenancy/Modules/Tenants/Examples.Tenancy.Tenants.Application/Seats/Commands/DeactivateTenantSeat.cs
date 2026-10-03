using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Commands;

/// <summary>
/// Deactivates a seat for good.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.seats.manage</c> for the whole tenant, and for what
/// taking each of the seat's roles away would need; and it keeps the tenant an administrator.
/// </remarks>
/// <param name="Seat">The seat.</param>
public sealed record DeactivateTenantSeat(SeatId Seat) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="DeactivateTenantSeat"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's use cases that change a seat.</param>
public sealed class DeactivateTenantSeatHandler(SampleTenancy.SeatCommands seats) : ICommandHandler<DeactivateTenantSeat>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(DeactivateTenantSeat command, CancellationToken cancellationToken)
    {
        await seats.DeactivateAsync(command.Seat, cancellationToken);
        return Unit.Value;
    }
}
