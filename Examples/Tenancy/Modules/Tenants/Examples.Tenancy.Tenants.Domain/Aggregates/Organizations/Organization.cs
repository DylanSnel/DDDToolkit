using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;

namespace Examples.Tenancy.Tenants.Domain.Aggregates.Organizations;

/// <summary>
/// The application's organization: a tenant's tree of units, exactly as the package ships it. Its id is the
/// tenant's, and its units are <see cref="OrganizationUnit"/>s, which the generator finds by their template.
/// </summary>
[OrganizationAggregate<TenantId>]
public sealed partial class Organization;
