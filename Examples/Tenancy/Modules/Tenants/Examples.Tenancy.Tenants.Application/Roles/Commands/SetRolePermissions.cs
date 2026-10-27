using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Sets the permissions a role grants, for every seat that holds it. The permissions become exactly the list: an empty one
/// takes every permission off the role.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.roles.manage</c> for the whole tenant. The package's use case asks for that permission again, and
/// that only an administrator adds or takes out a permission that manages access.
/// </remarks>
/// <param name="Role">The role.</param>
/// <param name="Permissions">The permissions it grants from now on.</param>
[RequiresPermission(TenancyPermissions.RolesManage)]
public sealed record SetRolePermissions(RoleId Role, IReadOnlyList<string> Permissions) : ICommand, ITenantsRequest;

/// <summary>Handles <see cref="SetRolePermissions"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's use cases that change a role.</param>
public sealed class SetRolePermissionsHandler(RoleManager roles) : ICommandHandler<SetRolePermissions>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(SetRolePermissions command, CancellationToken cancellationToken)
    {
        await roles.SetPermissionsAsync(command.Role, command.Permissions, cancellationToken);
        return Unit.Value;
    }
}
