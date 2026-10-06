using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Archives a role: it stays where it was granted, and grants nothing from now on.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.roles.manage</c> for the whole tenant. The package's use case asks for that key again, and
/// that only an administrator archives a role that manages access.
/// </remarks>
/// <param name="Role">The role.</param>
public sealed record ArchiveTenantRole(RoleId Role) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(TenancyKeys.RolesManage);
}

/// <summary>Handles <see cref="ArchiveTenantRole"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's use cases that change a role.</param>
public sealed class ArchiveTenantRoleHandler(TenantsTenancy.RoleCommands roles) : ICommandHandler<ArchiveTenantRole>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(ArchiveTenantRole command, CancellationToken cancellationToken)
    {
        await roles.ArchiveAsync(command.Role, cancellationToken);
        return Unit.Value;
    }
}
