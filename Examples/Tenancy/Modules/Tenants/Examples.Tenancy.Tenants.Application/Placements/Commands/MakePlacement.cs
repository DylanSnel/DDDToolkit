using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Placements.Commands;

/// <summary>
/// Places a seat in a unit.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.seats.manage</c> at the unit, which the request names. The package's use case asks for
/// that key again, and refuses a seat that places itself.
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="Unit">The unit, which must be active.</param>
/// <param name="Primary">Whether this becomes the seat's primary placement.</param>
public sealed record MakePlacement(SeatId Seat, OrganizationUnitId Unit, bool Primary) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(TenancyKeys.SeatsManage, Unit);
}

/// <summary>Handles <see cref="MakePlacement"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's use cases that change a seat.</param>
public sealed class MakePlacementHandler(SampleTenancy.SeatCommands seats) : ICommandHandler<MakePlacement>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(MakePlacement command, CancellationToken cancellationToken)
    {
        await seats.PlaceAsync(command.Seat, command.Unit, command.Primary, cancellationToken);
        return Unit.Value;
    }
}
