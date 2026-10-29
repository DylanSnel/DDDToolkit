using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Archives a role: it stays where it was granted, and grants nothing from now on.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.roles.manage</c> for the whole tenant. The package's manager asks for that permission again, and
/// that only an administrator archives a role that manages access.
/// </remarks>
/// <param name="Role">The role.</param>
[RequiresPermission(TenancyPermissions.RolesManage)]
public sealed record ArchiveTenantRole(RoleId Role) : ICommand<Role>, ITenantsRequest;

/// <summary>Handles <see cref="ArchiveTenantRole"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's role manager.</param>
public sealed class ArchiveTenantRoleHandler(RoleManager roles) : ICommandHandler<ArchiveTenantRole, Role>
{
    /// <inheritdoc />
    /// <returns>The role, archived.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<Role> Handle(ArchiveTenantRole command, CancellationToken cancellationToken)
        => await roles.ArchiveAsync(command.Role, cancellationToken);
}
