using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Commands;

/// <summary>
/// Adds a unit to the organization, below another, of the kind given.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.units.manage</c> at the parent, which the request names, and the package's manager asks
/// for that permission again. The kind is the application's own field on its unit class, which the package does not know:
/// the manager hands the new unit to a callback before it saves, and the handler sets the kind there, so it is
/// written in the same save.
/// </remarks>
/// <param name="Parent">The unit it hangs under.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">What kind of unit it is, or <see langword="null"/> to leave it unsaid.</param>
[RequiresPermission(TenancyPermissions.UnitsManage)]
public sealed record AddOrganizationUnit([AtUnit] OrganizationUnitId Parent, string Name, UnitKind? Kind) : ICommand<OrganizationUnit>, ITenantsRequest;

/// <summary>Handles <see cref="AddOrganizationUnit"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="organization">The package's organization manager.</param>
public sealed class AddOrganizationUnitHandler(OrganizationManager organization) : ICommandHandler<AddOrganizationUnit, OrganizationUnit>
{
    /// <inheritdoc />
    /// <returns>The new unit, with its kind.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<OrganizationUnit> Handle(AddOrganizationUnit command, CancellationToken cancellationToken)
        => await organization.AddUnitAsync(command.Parent, command.Name, cancellationToken, configure: unit => unit.SetKind(command.Kind));
}
