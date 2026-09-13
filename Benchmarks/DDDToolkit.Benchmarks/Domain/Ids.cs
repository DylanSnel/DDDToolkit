using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Benchmarks.Domain;

/// <summary>
/// The struct form of an identifier: a <c>readonly partial record struct</c> that is its value.
/// </summary>
[EntityId<Guid>("ORD")]
public readonly partial record struct StructOrderId;

/// <summary>
/// The record form of the same identifier, deriving from <c>EntityId&lt;Guid&gt;</c>. The generated
/// constructors are protected, so a benchmark needs a factory to build one from a known
/// <see cref="Guid"/>; <see cref="From"/> is that factory and nothing more.
/// </summary>
[EntityId<Guid>("ORD")]
public partial record RecordOrderId
{
    /// <summary>Wraps <paramref name="value"/> without going through <c>Parse</c>.</summary>
    public static RecordOrderId From(Guid value) => new(value);
}
