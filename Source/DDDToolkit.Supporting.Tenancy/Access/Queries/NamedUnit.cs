using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What a unit is called, and nothing more: its name, its path from the root and how deep it is. What
/// <c>OrganizationManager.UnitsByIdAsync</c> answers about the units a caller works on, whether or not it reads the rest of
/// the organization: a seat on a project at a unit it is not placed under, or one an application's restrictive rule
/// keeps out of the organization, still reads what that unit is called.
/// </summary>
/// <param name="Id">The unit.</param>
/// <param name="Name">The unit's name.</param>
/// <param name="Path">The names of the unit and the units above it, the root first, joined with <c>" / "</c>.</param>
/// <param name="Depth">How deep the unit is, the root being 1.</param>
public sealed record NamedUnit<TUnitId>(TUnitId Id, string Name, string Path, int Depth)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
