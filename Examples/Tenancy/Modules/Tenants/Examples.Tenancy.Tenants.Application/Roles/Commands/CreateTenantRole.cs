using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Makes a role by hand, from no starter role.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.roles.manage</c> for the whole tenant. The package's manager asks for that permission again, and
/// that the catalogue knows every permission.
/// </remarks>
/// <param name="Name">Its name, unique in the tenant.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Permissions">The permissions it grants; the permissions they imply are added.</param>
[RequiresPermission(TenancyPermissions.RolesManage)]
public sealed record CreateTenantRole(string Name, string Description, IReadOnlyList<string> Permissions) : ICommand<Role>, ITenantsRequest;

/// <summary>Handles <see cref="CreateTenantRole"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's role manager.</param>
public sealed class CreateTenantRoleHandler(RoleManager roles) : ICommandHandler<CreateTenantRole, Role>
{
    /// <inheritdoc />
    /// <returns>The new role.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<Role> Handle(CreateTenantRole command, CancellationToken cancellationToken)
        => await roles.CreateAsync(command.Name, command.Description, command.Permissions, cancellationToken);
}
