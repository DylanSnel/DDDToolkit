using Mediator;

namespace Examples.Tenancy.Tenants.Application.Grants.Commands;

/// <summary>
/// Takes a role away from a seat at a unit.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.grants.manage</c> at the unit, and for a role that
/// manages access, that the caller holds its keys that do, there and until the grant's end; and it keeps the tenant
/// an administrator.
/// </remarks>
/// <param name="Seat">The seat.</param>
/// <param name="Unit">The unit the role is held at.</param>
/// <param name="Role">The role.</param>
public sealed record RevokeGrant(SeatId Seat, OrganizationUnitId Unit, RoleId Role) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="RevokeGrant"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="seats">The package's use cases that change a seat.</param>
public sealed class RevokeGrantHandler(SampleTenancy.SeatCommands seats) : ICommandHandler<RevokeGrant>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(RevokeGrant command, CancellationToken cancellationToken)
    {
        await seats.RevokeAsync(command.Seat, command.Unit, command.Role, cancellationToken);
        return Unit.Value;
    }
}
