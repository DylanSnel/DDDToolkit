using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>The application's role, as the package ships it.</summary>
[RoleAggregate<RoleId>]
public sealed partial class HostRole;
