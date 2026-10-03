using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

// The application's ids. The tenant's is a long, to prove the package forces no Guid on anyone.

/// <summary>A tenant's id, and its organization's.</summary>
[EntityId<long>]
public readonly partial record struct TenantId;

/// <summary>A seat's id.</summary>
[EntityId<Guid>]
public readonly partial record struct SeatId;

/// <summary>An organization unit's id.</summary>
[EntityId<Guid>]
public readonly partial record struct OrganizationUnitId;

/// <summary>A role's id.</summary>
[EntityId<Guid>]
public readonly partial record struct RoleId;

/// <summary>An invitation's id.</summary>
[EntityId<Guid>]
public readonly partial record struct InvitationId;
