using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Validation;

namespace DDDToolkit.HotChocolate.Tests.Harbor.Domain;

// One id over each value a Relay node id can carry, a class id for its always-valid twin, and a single value
// object with a rule. None of them has a nested ChangeTypeProvider or NodeIdValueSerializer: this project does
// not reference HotChocolate, so the HotChocolate generator never runs over it.

/// <summary>A struct id over a <see cref="Guid"/>, with a prefix: it binds to <c>UUID</c>, and only a real one prints <c>VSL_</c>.</summary>
[EntityId<Guid>("VSL")]
public readonly partial record struct VesselId
{
    /// <summary>An id over the given value.</summary>
    public static VesselId Create(Guid value) => new(value);
}

/// <summary>A class id over a <see cref="Guid"/>, so it has an always-valid twin, <c>ValidSkipperId</c>.</summary>
[EntityId<Guid>("SKP")]
public partial record SkipperId
{
    /// <summary>An id over the given value.</summary>
    public static SkipperId Create(Guid value) => new(value);
}

/// <summary>A struct id over an <see cref="int"/>: it binds to <c>Int</c>.</summary>
[EntityId<int>]
public readonly partial record struct BerthNumber
{
    /// <summary>An id over the given value.</summary>
    public static BerthNumber Create(int value) => new(value);
}

/// <summary>A struct id over a <see cref="string"/>: it binds to <c>String</c>.</summary>
[EntityId<string>]
public readonly partial record struct QuayCode
{
    /// <summary>An id over the given value.</summary>
    public static QuayCode Create(string value) => new(value);
}

/// <summary>A struct id over a <see cref="long"/>: it binds to <c>Long</c>.</summary>
[EntityId<long>]
public readonly partial record struct VoyageNumber
{
    /// <summary>An id over the given value.</summary>
    public static VoyageNumber Create(long value) => new(value);
}

/// <summary>A struct id over a <see cref="short"/>: it binds to <c>Short</c>.</summary>
[EntityId<short>]
public readonly partial record struct MooringNumber
{
    /// <summary>An id over the given value.</summary>
    public static MooringNumber Create(short value) => new(value);
}

/// <summary>
/// A single value object with a rule, so its always-valid twin, <c>ValidCallSign</c>, has something to refuse: a
/// call sign is three to seven capital letters and digits.
/// </summary>
[SingleValueObject<string>]
public partial record CallSign
{
    /// <summary>A call sign over the given text, not yet checked.</summary>
    public static CallSign Create(string value) => new(value);

    /// <inheritdoc />
    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (Value is not { Length: >= 3 and <= 7 } || !Value.All(static character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character)))
        {
            errors.Add("A call sign is three to seven capital letters and digits.", nameof(Value), "NotACallSign");
        }
    }
}
