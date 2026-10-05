using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Commands;

/// <summary>
/// Adds a unit to the organization, below another, of the kind given.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.units.manage</c> at the parent, which the request names, and the package's use case asks
/// for that key again. The kind is the application's own field on its unit class, which the package does not know:
/// the use case hands the new unit to a callback before it saves, and the handler sets the kind there, so it is
/// written in the same save.
/// </remarks>
/// <param name="Parent">The unit it hangs under.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">What kind of unit it is, or <see langword="null"/> to leave it unsaid.</param>
public sealed record AddOrganizationUnit(OrganizationUnitId Parent, string Name, UnitKind? Kind) : ICommand<OrganizationUnitId>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(TenancyKeys.UnitsManage, Parent);
}

/// <summary>Handles <see cref="AddOrganizationUnit"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="organization">The package's use cases that change the organization.</param>
public sealed class AddOrganizationUnitHandler(SampleTenancy.OrganizationCommands organization) : ICommandHandler<AddOrganizationUnit, OrganizationUnitId>
{
    /// <inheritdoc />
    /// <returns>The new unit's id.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<OrganizationUnitId> Handle(AddOrganizationUnit command, CancellationToken cancellationToken)
        => await organization.AddUnitAsync(command.Parent, command.Name, cancellationToken, configure: unit => unit.SetKind(command.Kind));
}
