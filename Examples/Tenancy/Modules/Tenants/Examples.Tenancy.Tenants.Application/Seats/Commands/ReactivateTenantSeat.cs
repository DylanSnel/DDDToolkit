using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Commands;

/// <summary>
/// Makes a suspended seat active again, with every grant that has not ended.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.seats.manage</c> for the whole tenant, and for what
/// giving each of the seat's roles would need.
/// </remarks>
/// <param name="Seat">The seat.</param>
public sealed record ReactivateTenantSeat(SeatId Seat) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="ReactivateTenantSeat"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's use cases that change a seat.</param>
public sealed class ReactivateTenantSeatHandler(SampleTenancy.SeatCommands seats) : ICommandHandler<ReactivateTenantSeat>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(ReactivateTenantSeat command, CancellationToken cancellationToken)
    {
        await seats.ReactivateAsync(command.Seat, cancellationToken);
        return Unit.Value;
    }
}
