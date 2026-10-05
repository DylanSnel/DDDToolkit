using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Commands;

/// <summary>
/// Adds a unit to the organization, below another.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.units.manage</c> at the parent, which the request names. The package's use case asks for
/// that key again, and that the kind is one of the application's.
/// </remarks>
/// <param name="Parent">The unit it hangs under.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Its kind, one of the catalogue's unit kinds.</param>
public sealed record AddOrganizationUnit(OrganizationUnitId Parent, string Name, string Kind) : ICommand<OrganizationUnitId>, ITenantsRequest
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
        => await organization.AddUnitAsync(command.Parent, command.Name, command.Kind, cancellationToken);
}
