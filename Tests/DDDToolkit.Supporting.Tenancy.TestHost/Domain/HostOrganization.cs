using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>The application's organization, as the package ships it.</summary>
[OrganizationAggregate<TenantId>]
public sealed partial class HostOrganization;
