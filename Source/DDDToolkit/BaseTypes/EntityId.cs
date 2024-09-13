using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;

public abstract record EntityId<TIdType> : SingleValueObject<TIdType>, IEntityId where TIdType : notnull
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
    public sealed override string ToString() => $"{_prefix}{(string.IsNullOrEmpty(_prefix) ? "" : "_")}{Value}";


}

