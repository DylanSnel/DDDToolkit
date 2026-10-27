using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Placements.Commands;

/// <summary>
/// Places a seat in a unit.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.seats.manage</c> at the unit, which the request names. The package's manager asks for
/// that permission again, and refuses a seat that places itself.
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="Unit">The unit, which must be active.</param>
/// <param name="Primary">Whether this becomes the seat's primary placement.</param>
[RequiresPermission(TenancyPermissions.SeatsManage)]
public sealed record MakePlacement(SeatId Seat, [AtUnit] OrganizationUnitId Unit, bool Primary) : ICommand<Seat>, ITenantsRequest;

/// <summary>Handles <see cref="MakePlacement"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's seat manager.</param>
public sealed class MakePlacementHandler(SeatManager seats) : ICommandHandler<MakePlacement, Seat>
{
    /// <inheritdoc />
    /// <returns>The seat, with its new placement.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<Seat> Handle(MakePlacement command, CancellationToken cancellationToken)
        => await seats.PlaceAsync(command.Seat, command.Unit, command.Primary, cancellationToken);
}
