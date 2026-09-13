using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Benchmarks.Domain;

/// <summary>
/// An aggregate keyed by the struct identifier. Deliberately tiny: the benchmark is about the key,
/// so everything else has to be identical to <see cref="RecordOrder"/>.
/// </summary>
[AggregateRoot<StructOrderId>]
public partial class StructOrder
{
    /// <summary>Creates an order with the given key and reference.</summary>
    public StructOrder(StructOrderId id, string reference) : base(id) => Reference = reference;

    /// <summary>A payload column, so the row is not just the key.</summary>
    public string Reference { get; private set; }
}

/// <summary>
/// The same aggregate keyed by the record identifier.
/// </summary>
[AggregateRoot<RecordOrderId>]
public partial class RecordOrder
{
    /// <summary>Creates an order with the given key and reference.</summary>
    public RecordOrder(RecordOrderId id, string reference) : base(id) => Reference = reference;

    /// <summary>A payload column, so the row is not just the key.</summary>
    public string Reference { get; private set; }
}
