using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Archives a role: it stays where it was granted, and grants nothing from now on.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.roles.manage</c> for the whole tenant, and that only
/// an administrator archives a role that manages access.
/// </remarks>
/// <param name="Role">The role.</param>
public sealed record ArchiveTenantRole(RoleId Role) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="ArchiveTenantRole"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's use cases that change a role.</param>
public sealed class ArchiveTenantRoleHandler(SampleTenancy.RoleCommands roles) : ICommandHandler<ArchiveTenantRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(ArchiveTenantRole command, CancellationToken cancellationToken)
    {
        await roles.ArchiveAsync(command.Role, cancellationToken);
        return Unit.Value;
    }
}
