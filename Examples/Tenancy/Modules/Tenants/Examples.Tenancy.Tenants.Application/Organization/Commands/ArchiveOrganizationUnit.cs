using Mediator;

namespace Examples.Tenancy.Tenants.Application.Organization.Commands;

/// <summary>
/// Archives a unit: nothing new is placed or granted there, and what is there keeps working.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.units.manage</c> at the unit's parent.
/// </remarks>
/// <param name="Unit">The unit to archive.</param>
public sealed record ArchiveOrganizationUnit(OrganizationUnitId Unit) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="ArchiveOrganizationUnit"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="organization">The package's use cases that change the organization.</param>
public sealed class ArchiveOrganizationUnitHandler(SampleTenancy.OrganizationCommands organization) : ICommandHandler<ArchiveOrganizationUnit>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(ArchiveOrganizationUnit command, CancellationToken cancellationToken)
    {
        await organization.ArchiveUnitAsync(command.Unit, cancellationToken);
        return Unit.Value;
    }
}
