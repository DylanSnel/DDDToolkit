using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.BaseTypes;

public abstract class EntityId<TIdType> : SingleValueObject<TIdType>, IEntityId where TIdType : notnull
{
    private readonly string _prefix;
    protected EntityId(TIdType value, string prefix = "") : base(value)
    {
        _prefix = prefix;
    }

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
    public override string? ToString() => $"{_prefix}_{Value?.ToString() ?? base.ToString()}";

#pragma warning disable CS8618
    protected EntityId()
    {
    }
#pragma warning restore CS8618
}

