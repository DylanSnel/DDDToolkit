using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Tests.Domain;

/// <summary>
/// A prefixed <see cref="Guid"/> struct id. Exercises the whole generated struct surface:
/// <c>Value</c>, <c>Empty</c>/<c>IsEmpty</c>, <c>CreateUnique</c>/<c>CreateSequential</c>,
/// <c>ToString</c> as <c>BSK_{guid}</c>, <c>Parse</c>/<c>TryParse</c> and the explicit conversions.
/// </summary>
[EntityId<Guid>("BSK")]
public readonly partial record struct BasketId
{
    /// <summary>Wraps an existing value. The generated constructor is public; this reads better at call sites.</summary>
    public static BasketId Create(Guid value) => new(value);
}

/// <summary>
/// An <see cref="int"/> struct id. Integers have no <c>CreateUnique</c>; the generator only emits the
/// Guid factories. Ordering is the natural numeric ordering, which makes it the clearest id to sort with.
/// </summary>
[EntityId<int>("LINE")]
public readonly partial record struct BasketLineId
{
    /// <summary>Wraps an existing value.</summary>
    public static BasketLineId Create(int value) => new(value);
}

/// <summary>
/// A <see cref="string"/> struct id. String ids parse differently from every other kind: any input is a
/// legal value, so <c>TryParse</c> only fails on <see langword="null"/>. <c>StructEntityIdTests</c>
/// pins what that means for an unexpected prefix.
/// </summary>
[EntityId<string>("SKU")]
public readonly partial record struct Sku
{
    /// <summary>Wraps an existing value.</summary>
    public static Sku Create(string value) => new(value);
}

/// <summary>An unprefixed <see cref="Guid"/> struct id: <c>ToString()</c> is the bare value.</summary>
[EntityId<Guid>]
public readonly partial record struct TagId
{
    /// <summary>Wraps an existing value.</summary>
    public static TagId Create(Guid value) => new(value);
}

/// <summary>
/// A reference-type id: <c>partial record</c> rather than <c>readonly partial record struct</c>. It derives
/// from <c>EntityId&lt;Guid&gt;</c> and gains an always-valid <c>ValidLedgerId</c> twin, which struct ids do not.
/// </summary>
[EntityId<Guid>("LDG")]
public partial record LedgerId
{
    /// <summary>Wraps an existing value.</summary>
    public static LedgerId Create(Guid value) => new(value);
}
