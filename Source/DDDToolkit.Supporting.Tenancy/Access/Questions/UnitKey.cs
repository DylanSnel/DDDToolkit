using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// A unit and a key the caller holds there now: one answer of
/// <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.WhereIHold"/>, which answers such a pair for
/// every unit a key reaches, the units it is held at and every unit below them.
/// </summary>
/// <param name="Unit">The unit.</param>
/// <param name="Key">The key held there.</param>
public readonly record struct UnitKey<TUnitId>(TUnitId Unit, string Key)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
