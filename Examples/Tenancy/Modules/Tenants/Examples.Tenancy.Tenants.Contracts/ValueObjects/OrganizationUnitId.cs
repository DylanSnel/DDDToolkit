using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Contracts.ValueObjects;

/// <summary>An organization unit's id: the root, a region, an area or a site.</summary>
[EntityId<Guid>("UNIT")]
public readonly partial record struct OrganizationUnitId;
