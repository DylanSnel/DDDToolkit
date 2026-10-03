using Mediator;

namespace Examples.Tenancy.Tenants.Application.Tenant.Commands;

/// <summary>
/// Changes the shape of the caller's tenant: flat to hierarchical, never back. The tenant gains a role for
/// every pack of the new shape it has no copy of yet.
/// </summary>
/// <remarks>
/// The package decides who may: its use case asks for <c>tenancy.settings.manage</c> for the whole tenant.
/// </remarks>
/// <param name="Shape">The new shape.</param>
public sealed record ChangeTenantShape(TenantShape Shape) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Handles <see cref="ChangeTenantShape"/> with the Tenancy package's use case, which checks the caller, decides and saves.</summary>
/// <param name="tenants">The package's use cases that change a tenant.</param>
public sealed class ChangeTenantShapeHandler(SampleTenancy.TenantCommands tenants) : ICommandHandler<ChangeTenantShape>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(ChangeTenantShape command, CancellationToken cancellationToken)
    {
        await tenants.ChangeShapeAsync(command.Shape, roleIds: null, language: null, cancellationToken);
        return Unit.Value;
    }
}
