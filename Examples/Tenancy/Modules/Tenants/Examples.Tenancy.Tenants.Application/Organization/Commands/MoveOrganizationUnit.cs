using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Commands;

/// <summary>
/// Hangs a unit, with everything below it, under another parent.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. The rest is for the package's manager to ask, since only it reads
/// where the unit hangs now: <c>tenancy.units.manage</c> at the old parent and at the new one, and that the move
/// gives or takes away nothing the caller could not.
/// </remarks>
/// <param name="Unit">The unit to move.</param>
/// <param name="Parent">The unit it hangs under from now on.</param>
[InTenant]
public sealed record MoveOrganizationUnit(OrganizationUnitId Unit, OrganizationUnitId Parent) : ICommand<OrganizationUnit>, ITenantsRequest;

/// <summary>Handles <see cref="MoveOrganizationUnit"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="organization">The package's organization manager.</param>
public sealed class MoveOrganizationUnitHandler(OrganizationManager organization) : ICommandHandler<MoveOrganizationUnit, OrganizationUnit>
{
    /// <inheritdoc />
    /// <returns>The unit, under its new parent.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<OrganizationUnit> Handle(MoveOrganizationUnit command, CancellationToken cancellationToken)
        => await organization.MoveUnitAsync(command.Unit, command.Parent, cancellationToken);
}
