using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Contracts.ValueObjects;

/// <summary>A role's id. A crew member's roles each carry one, so Projects stores it too.</summary>
[EntityId<Guid>("ROLE")]
public readonly partial record struct RoleId;
