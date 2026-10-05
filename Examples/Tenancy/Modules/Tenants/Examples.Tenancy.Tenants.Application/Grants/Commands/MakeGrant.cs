using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Grants.Commands;

/// <summary>
/// Gives a seat a role at a unit where it is placed, from now until a moment, or for good. It has no start:
/// the package lets only system work pick one, and no request made through here is.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.grants.manage</c> at the unit, which the request names, so a caller without it is refused
/// before the handler runs. The package's use case asks for that key again, and then for what only it can read: for
/// a role that manages access, that the caller holds its keys that do, there and for at least as long, and is not
/// giving it to itself.
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="Unit">The unit of one of its placements.</param>
/// <param name="Role">The role, which must be active.</param>
/// <param name="Until">When the grant ends, or <see langword="null"/> for no end.</param>
/// <param name="Reason">Why, or <see langword="null"/>.</param>
public sealed record MakeGrant(SeatId Seat, OrganizationUnitId Unit, RoleId Role, DateTimeOffset? Until, string? Reason) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(TenancyKeys.GrantsManage, Unit);
}

/// <summary>Handles <see cref="MakeGrant"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's use cases that change a seat.</param>
public sealed class MakeGrantHandler(SampleTenancy.SeatCommands seats) : ICommandHandler<MakeGrant>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(MakeGrant command, CancellationToken cancellationToken)
    {
        await seats.GrantAsync(command.Seat, command.Unit, command.Role, command.Until, command.Reason, cancellationToken);
        return Unit.Value;
    }
}
