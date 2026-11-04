using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Benchmarks.Domain;

/// <summary>
/// A value object with two compared properties, one a value type and one a reference type: the shape the
/// equality of a <c>[ValueObject]</c> is measured on.
/// </summary>
[ValueObject]
public partial record Money(decimal Amount, string Currency);
