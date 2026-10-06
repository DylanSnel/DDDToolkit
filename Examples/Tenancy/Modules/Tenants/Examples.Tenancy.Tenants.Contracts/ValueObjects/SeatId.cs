using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Contracts.ValueObjects;

/// <summary>A seat's id: one person's place in one tenant.</summary>
[EntityId<Guid>("SEAT")]
public readonly partial record struct SeatId;
