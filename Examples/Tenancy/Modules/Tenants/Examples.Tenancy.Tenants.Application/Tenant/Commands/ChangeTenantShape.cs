using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Tenant.Commands;

/// <summary>
/// Changes the shape of the caller's tenant: flat to hierarchical, never back. The tenant gains a role for
/// every pack seeded for the new shape that it has no copy of yet.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.settings.manage</c> for the whole tenant, which the package's use case asks for again.
/// </remarks>
/// <param name="Shape">The new shape.</param>
public sealed record ChangeTenantShape(TenantShape Shape) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(TenancyKeys.SettingsManage);
}

/// <summary>Handles <see cref="ChangeTenantShape"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="tenants">The package's use cases that change a tenant.</param>
public sealed class ChangeTenantShapeHandler(TenantsTenancy.TenantCommands tenants) : ICommandHandler<ChangeTenantShape>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(ChangeTenantShape command, CancellationToken cancellationToken)
    {
        await tenants.ChangeShapeAsync(command.Shape, roleIds: null, language: null, cancellationToken);
        return Unit.Value;
    }
}
