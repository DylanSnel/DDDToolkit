using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// A right a move of a unit would change: where it is held, the key, until when, which of the two parents it
/// reaches, the one the unit hangs under or the one it would, and whether it is the moving seat's own. What a
/// move's check reads of the rights of every seat, and no more of them: no seat, and no role.
/// </summary>
/// <param name="UnitId">The unit the right is held at: a parent, or a unit above one.</param>
/// <param name="Key">The key.</param>
/// <param name="EndsAt">The first moment the right no longer applies, or <see langword="null"/> when it has no end.</param>
/// <param name="Parent">The parent it reaches. A right held above both parents comes once for each.</param>
/// <param name="OfCaller">Whether the right is the moving seat's own.</param>
public sealed record MoveReach<TUnitId>(TUnitId UnitId, string Key, DateTimeOffset? EndsAt, TUnitId Parent, bool OfCaller)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
