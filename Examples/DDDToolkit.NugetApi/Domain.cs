using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;
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
/// A positional value object. The packaged generator has to declare its properties itself, and the
/// packaged suppressor has to silence the CS0657 the carried-over <c>[property: DontCompare]</c> would
/// otherwise raise: this project treats warnings as errors, so either one missing fails the build.
/// </summary>
[ValueObject]
public partial record Money(decimal Amount, string Currency, [property: DontCompare] string? Note);

/// <summary>
/// An aggregate root whose identifier is generated from the attribute rather than declared, with a
/// read-only collection and both shapes of invariant. Exercises the entity generator, the implicit
/// identifier path, the nested rule the generator has to discover, and the seam in one declaration.
/// </summary>
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id) => Customer = customer;

    public CustomerId Customer { get; private set; }

    public partial IReadOnlyList<Sku> Lines { get; }

    /// <summary>
    /// A collection of child entities, which is what makes this aggregate answer for something other
    /// than itself. <see cref="Lines"/> is a collection of identifiers and is walked by nothing; this
    /// one makes the generator emit the walk, so the walk has to compile from a packaged generator.
    /// </summary>
    public partial IReadOnlyList<OrderNote> Notes { get; }

    public void AddLine(Sku sku) => _lines.Add(sku);

    public void AddNote(string text) => _notes.Add(new OrderNote(OrderNoteId.CreateUnique(), text));

    /// <summary>
    /// A named rule, nested so the generator can find it. Discovery is the part worth verifying
    /// against a package: the generator reads this type's nested types, so a rule that is never
    /// collected leaves <c>GetInvariantViolations()</c> returning nothing and says nothing about it.
    /// <c>Check.cs</c> names the members instead, and the compiler asserts they arrived.
    /// </summary>
    public sealed class MustHaveLines : IInvariant<Order>
    {
        public const string ViolationCode = "ORD_NO_LINES";

        public string Code => ViolationCode;

        public string? Check(Order order) => order._lines.Count == 0 ? "An order must have at least one line." : null;
    }

    /// <summary>
    /// The other shape, kept alongside the rule above because the generator has to keep emitting
    /// both. A <c>partial void</c> the compiler would erase if nobody implemented it is exactly the
    /// kind of member a packaging mistake makes disappear without a word.
    /// </summary>
    partial void CheckInvariants()
    {
        if (Customer.IsEmpty)
        {
            throw InvariantViolation("An order must name a customer.");
        }
    }
}

/// <summary>
/// A child entity, so the aggregate above has something to answer for. Its identifier is generated
/// from the attribute, like the order's, and its rule is reported by <see cref="Order"/> rather than
/// by anything that knows this class exists.
/// </summary>
[Entity<Guid>("NOTE")]
public partial class OrderNote
{
    public OrderNote(OrderNoteId id, string text) : base(id) => Text = text;

    public string Text { get; private set; } = string.Empty;

    /// <summary>
    /// A rule of the child. What it verifies here is not the rule, which is nonsense, but that a
    /// packaged generator emitted a walk over <c>Order.Notes</c> that compiles, and the four members
    /// the walk is reached through.
    /// </summary>
    public sealed class MustSaySomething : IInvariant<OrderNote>
    {
        public const string ViolationCode = "NOTE_IS_EMPTY";

        public string Code => ViolationCode;

        public string? Check(OrderNote note) => string.IsNullOrWhiteSpace(note.Text) ? "A note must say something." : null;
    }
}
