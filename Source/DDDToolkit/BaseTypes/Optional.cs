namespace DDDToolkit.BaseTypes;

/// <summary>
/// An argument that may be left out, for the generated <c>With(...)</c> of a value object. Leaving it out
/// keeps the current value; passing anything, <see langword="null"/> included, replaces it.
/// </summary>
/// <remarks>
/// A plain optional parameter cannot tell "not given" from "given as null", and a value object with a
/// nullable property needs both. You never write this type: a value converts to it implicitly, so a
/// call reads <c>money.With(amount: 5)</c> or <c>name.With(middleNames: null)</c>.
/// </remarks>
public readonly struct Optional<T>
{
    private readonly T _value;

    private Optional(T value)
    {
        _value = value;
        HasValue = true;
    }

    /// <summary>True when a value was passed, even <see langword="null"/>.</summary>
    public bool HasValue { get; }

    /// <summary>The value passed, or <paramref name="current"/> when none was.</summary>
    public T Or(T current) => HasValue ? _value : current;

    public static implicit operator Optional<T>(T value) => new(value);

    public override string ToString() => HasValue ? _value?.ToString() ?? "null" : "(not given)";
}
