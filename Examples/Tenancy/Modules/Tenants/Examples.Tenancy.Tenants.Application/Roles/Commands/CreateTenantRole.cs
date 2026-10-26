using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Makes a role by hand, from no pack.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.roles.manage</c> for the whole tenant. The package's use case asks for that permission again, and
/// that the catalogue knows every permission.
/// </remarks>
/// <param name="Name">Its name, unique in the tenant.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Permissions">The permissions it grants; the permissions they imply are added.</param>
[RequiresPermission(TenancyPermissions.RolesManage)]
public sealed record CreateTenantRole(string Name, string Description, IReadOnlyList<string> Permissions) : ICommand<RoleId>, ITenantsRequest;

/// <summary>Handles <see cref="CreateTenantRole"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's use cases that change a role.</param>
public sealed class CreateTenantRoleHandler(TenancyUseCases.RoleCommands roles) : ICommandHandler<CreateTenantRole, RoleId>
{
    /// <inheritdoc />
    /// <returns>The new role's id.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<RoleId> Handle(CreateTenantRole command, CancellationToken cancellationToken)
        => await roles.CreateAsync(command.Name, command.Description, command.Permissions, cancellationToken);
}
