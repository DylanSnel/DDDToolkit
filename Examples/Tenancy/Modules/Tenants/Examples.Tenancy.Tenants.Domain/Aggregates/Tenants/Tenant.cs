namespace Examples.Tenancy.Tenants.Domain.Aggregates.Tenants;

/// <summary>
/// The application's tenant: the package's, closed over <see cref="TenantId"/>, with one field of its own.
/// </summary>
/// <remarks>
/// It declares no constructor. The package creates tenants itself, through the parameterless constructor the
/// generator writes, so provisioning is the same use case for every application; what the application adds
/// is state and behaviour, never a different way in. The rules about slugs, status and shape are the
/// package's, and nothing here can override them: they have no virtual members to override.
/// </remarks>
[TenantAggregate<TenantId>]
public sealed partial class Tenant
{
    /// <summary>
    /// Whether this tenant exists only to demonstrate the application: state of the application's own, which
    /// the package stores with the tenant and otherwise knows nothing about.
    /// </summary>
    public bool IsDemo { get; private set; }

    /// <summary>Marks the tenant as one for demonstrations.</summary>
    public void MarkAsDemo() => IsDemo = true;
}
