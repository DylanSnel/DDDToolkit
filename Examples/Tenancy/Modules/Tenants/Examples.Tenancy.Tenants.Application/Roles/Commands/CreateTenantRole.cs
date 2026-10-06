using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Roles.Commands;

/// <summary>
/// Makes a role by hand, from no pack.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.roles.manage</c> for the whole tenant. The package's use case asks for that key again, and
/// that the catalogue knows every key.
/// </remarks>
/// <param name="Name">Its name, unique in the tenant.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Keys">The keys it grants; the keys they imply are added.</param>
public sealed record CreateTenantRole(string Name, string Description, IReadOnlyList<string> Keys) : ICommand<RoleId>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(TenancyKeys.RolesManage);
}

/// <summary>Handles <see cref="CreateTenantRole"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="roles">The package's use cases that change a role.</param>
public sealed class CreateTenantRoleHandler(TenantsTenancy.RoleCommands roles) : ICommandHandler<CreateTenantRole, RoleId>
{
    /// <inheritdoc />
    /// <returns>The new role's id.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<RoleId> Handle(CreateTenantRole command, CancellationToken cancellationToken)
        => await roles.CreateAsync(command.Name, command.Description, command.Keys, cancellationToken);
}
