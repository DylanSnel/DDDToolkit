using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Tenant.Commands;

/// <summary>
/// Changes the shape of the caller's tenant: flat to hierarchical, never back. The tenant gains a role for
/// every starter role seeded for the new shape that it has no copy of yet.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.settings.manage</c> for the whole tenant, which the package's manager asks for again.
/// </remarks>
/// <param name="Shape">The new shape.</param>
[RequiresPermission(TenancyPermissions.SettingsManage)]
public sealed record ChangeTenantShape(TenantShape Shape) : ICommand<Domain.Aggregates.Tenants.Tenant>, ITenantsRequest;

/// <summary>Handles <see cref="ChangeTenantShape"/> with the Tenancy package's manager, which checks the caller, decides and saves.</summary>
/// <param name="tenants">The package's tenant manager.</param>
public sealed class ChangeTenantShapeHandler(TenantManager tenants) : ICommandHandler<ChangeTenantShape, Domain.Aggregates.Tenants.Tenant>
{
    /// <inheritdoc />
    /// <returns>The tenant, in its new shape.</returns>
    /// <exception cref="Exceptions.RefusalException">What the package's manager refuses, with its code.</exception>
    public async ValueTask<Domain.Aggregates.Tenants.Tenant> Handle(ChangeTenantShape command, CancellationToken cancellationToken)
        => await tenants.ChangeShapeAsync(command.Shape, roleIds: null, language: null, cancellationToken);
}
