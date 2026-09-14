using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// Base record for reference-type strongly typed ids (<c>[EntityId&lt;T&gt;] partial record</c>).
/// Struct ids do not derive from this; they implement <see cref="IEntityId{TValue}"/> directly.
/// </summary>
public abstract record EntityId<TIdType> : SingleValueObject<TIdType>, IEntityId<TIdType>
    where TIdType : notnull
{
    private readonly string _prefix;

    protected EntityId(TIdType value, string prefix = "") : base(value)
    {
        _prefix = prefix;
    }

    protected EntityId(string prefix = "")
    {
        _prefix = prefix;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>The id as text: <c>PREFIX_value</c>, or just the value when there is no prefix.</summary>
    public sealed override string ToString()
        => string.IsNullOrEmpty(_prefix) ? $"{Value}" : $"{_prefix}_{Value}";
}
