using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Commands;

/// <summary>
/// Archives a unit: nothing new is placed or granted there, and what is there keeps working.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. The permission is for the package's manager to ask, since only it reads
/// where the unit hangs: <c>tenancy.units.manage</c> at the unit's parent.
/// </remarks>
/// <param name="Unit">The unit to archive.</param>
[InTenant]
public sealed record ArchiveOrganizationUnit(OrganizationUnitId Unit) : ICommand<OrganizationUnit>, ITenantsRequest;

/// <summary>Handles <see cref="ArchiveOrganizationUnit"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="organization">The package's organization manager.</param>
public sealed class ArchiveOrganizationUnitHandler(OrganizationManager organization) : ICommandHandler<ArchiveOrganizationUnit, OrganizationUnit>
{
    /// <inheritdoc />
    /// <returns>The unit, archived.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<OrganizationUnit> Handle(ArchiveOrganizationUnit command, CancellationToken cancellationToken)
        => await organization.ArchiveUnitAsync(command.Unit, cancellationToken);
}
