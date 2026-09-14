using DDDToolkit.Abstractions.Attributes;
using FluentValidation;

namespace DDDToolkit.NugetApi;

// Everything here exists to make a generator run. Nothing is a domain worth reading; read
// Examples/ModularMonolith for that. What matters is that these declarations produce the same code
// when the toolkit arrives as packages as they do when the repository references the projects.

/// <summary>A struct identifier with a prefix, from DDDToolkit.Analyzers.</summary>
[EntityId<Guid>("CUST")]
public readonly partial record struct CustomerId;

/// <summary>A string-valued identifier, so the parsing branch of the generator runs too.</summary>
[EntityId<string>(Prefix: "SKU", ColumnLength: 32)]
public readonly partial record struct Sku;

/// <summary>A single value object whose rules come from DDDToolkit.FluentValidation.Analyzers.</summary>
[SingleValueObject<string>(ColumnLength: 255)]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);

    partial class Validator
    {
        public Validator() => RuleFor(x => x.Value).EmailAddress();
    }
}

/// <summary>A multi-property value object, for the structural equality generator.</summary>
[ValueObject]
public partial record Address
{
    public Address(string street, string city) => (Street, City) = (street, city);

    public string Street { get; protected init; }

    public string City { get; protected init; }
}

/// <summary>
/// An aggregate root whose identifier is generated from the attribute rather than declared, with a
/// read-only collection and an invariant. Exercises the entity generator, the implicit identifier
/// path and the invariant seam in one declaration.
/// </summary>
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id) => Customer = customer;

    public CustomerId Customer { get; private set; }

    public partial IReadOnlyList<Sku> Lines { get; }

    public void AddLine(Sku sku) => _lines.Add(sku);

    partial void CheckInvariants()
    {
        if (_lines.Count == 0)
        {
            throw InvariantViolation("An order must have at least one line.");
        }
    }
}
