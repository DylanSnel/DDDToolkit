using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;

namespace Campus.Tenants;

// The application's organization: a tenant is a college, with faculties and institutes below it, people who
// have a seat in it, and roles of its own. Each class is the package's, declared with its template under the
// application's own name and id, and adds nothing: what the suites are about is what stands beside it.

/// <summary>A tenant's id, and its organization's.</summary>
[EntityId<Guid>]
public readonly partial record struct TenantId;

/// <summary>A seat's id: what a member of a course or of a lab is known by.</summary>
[EntityId<Guid>]
public readonly partial record struct SeatId;

/// <summary>An organization unit's id: where a course or a lab sits.</summary>
[EntityId<Guid>]
public readonly partial record struct OrganizationUnitId;

/// <summary>The id of a role of the tenant's own: what a lab's member holds a role by.</summary>
[EntityId<Guid>]
public readonly partial record struct RoleId;

/// <summary>A college.</summary>
[TenantAggregate<TenantId>]
public sealed partial class Tenant;

/// <summary>A college's organization: its faculties and their institutes, in a tree.</summary>
[OrganizationAggregate<TenantId>]
public sealed partial class Organization;

/// <summary>A faculty or an institute.</summary>
[OrganizationUnit<OrganizationUnitId>]
public sealed partial class OrganizationUnit;

/// <summary>The place somebody has in a college.</summary>
[SeatAggregate<SeatId>]
public sealed partial class Seat;

/// <summary>A role a college makes for itself, given at a place in its organization.</summary>
[RoleAggregate<RoleId>]
public sealed partial class Role;
