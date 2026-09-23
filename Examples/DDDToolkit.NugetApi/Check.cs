using DDDToolkit.Interfaces;
using DDDToolkit.Invariants;
// TryToValid is an extension method, so this using is required even though the type it extends is
// generated into this project's own namespace.
using DDDToolkit.Validation;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.NugetApi;

/// <summary>
/// Names every generated member this project expects. Nothing calls it; the compiler is the assertion.
/// A generator that did not arrive, or arrived and produced something differently named, fails the
/// build here with a message that says which member is missing.
/// </summary>
internal static class Check
{
    /// <summary>DDDToolkit.Analyzers: the struct identifier surface.</summary>
    public static void StructIdentifier()
    {
        var id = CustomerId.CreateUnique();
        _ = id.Value;
        _ = id.IsEmpty;
        _ = CustomerId.Empty;
        _ = CustomerId.Parse(id.ToString());
        _ = CustomerId.TryParse("CUST_not-a-guid", out _);
        _ = (Guid)id;
        _ = id.CompareTo(id);
    }

    /// <summary>DDDToolkit.Analyzers: the identifier generated from [AggregateRoot&lt;Guid&gt;("ORD")].</summary>
    public static OrderId ImplicitIdentifier() => OrderId.CreateSequential();

    /// <summary>DDDToolkit.Analyzers: the value object twin, and the toolkit's own failure path.</summary>
    public static bool ValueObjects()
    {
        var email = EmailAddress.Create("someone@example.com");
        _ = email.ToValid();
        return email.TryToValid(out _, out _);
    }

    /// <summary>
    /// DDDToolkit.Analyzers: a positional value object and the generated <c>With(...)</c>, on the value
    /// object and on its twin. The arguments are <c>Optional&lt;T&gt;</c>, which ships in the DDDToolkit
    /// package, so this also fails if the runtime and the generator come from mismatched packages.
    /// </summary>
    public static Money PositionalValueObjects()
    {
        var money = new Money(10m, "EUR", Note: null);
        var (amount, currency, _) = money;

        ValidMoney valid = money.ToValid().With(amount: amount + 1, currency: currency);
        _ = new Address("Main Street", "Amsterdam").With(city: "Utrecht");

        return valid.With(note: null);
    }

    /// <summary>DDDToolkit.FluentValidation.Analyzers: the generated Errors collection.</summary>
    public static int ValidationErrors() => EmailAddress.Create("nope").Errors.Count;

    /// <summary>DDDToolkit.Analyzers: the base type, the event list and the invariant seam.</summary>
    public static void AggregateSurface()
    {
        var order = new Order(OrderId.CreateUnique(), CustomerId.CreateUnique());
        order.AddLine(new Sku("ABC"));
        order.AddNote("packed by hand");
        _ = order.Lines.Count;
        _ = order.Notes.Count;
        _ = order.Version;
        _ = ((IHasDomainEvents)order).DomainEvents;
        order.EnsureInvariants();
    }

    /// <summary>
    /// DDDToolkit.Analyzers: all four members of the invariant surface, and the types they are built
    /// from. Each is a generated override, so it disappears with the generator;
    /// <c>InvariantViolation</c> and <c>IInvariant&lt;T&gt;</c> ship in the DDDToolkit package itself,
    /// so they disappear with a mispacked one.
    /// </summary>
    public static string Invariants()
    {
        var order = new Order(OrderId.CreateUnique(), CustomerId.CreateUnique());

        // The check stage over the whole aggregate: generated, non-throwing, and typed on the toolkit's
        // own violation record. Named twice, because persistence reaches it through the interface and
        // you reach it directly.
        IReadOnlyList<InvariantViolation> violations = order.GetInvariantViolations();
        _ = ((IHasInvariants)order).GetInvariantViolations();
        _ = InvariantViolation.SeamCode;

        // The self-only half of each stage, which is what a caller already walking the graph asks, and
        // what DDDToolkit.EntityFramework's interceptor calls. A package that shipped the walking pair
        // alone would leave every save asking nothing, and fails here instead.
        _ = order.GetOwnInvariantViolations();
        _ = ((IHasInvariants)order).GetOwnInvariantViolations();
        order.EnsureOwnInvariants();

        // The nested rules are ordinary types, so naming them here proves they compiled. Whether the
        // generator found them is what the lines above answer: a rule nobody collected reports nothing.
        // MustSaySomething belongs to the child, and only the walk over Order.Notes can report it.
        _ = new Order.MustHaveLines().Check(order);
        _ = new OrderNote.MustSaySomething().Check(new OrderNote(OrderNoteId.CreateUnique(), "note"));

        // EntityType and EntityId are how a caller tells which child reported a violation, so they are
        // as much a part of the shipped surface as the members that hand them over.
        return violations.Count == 0
            ? Order.MustHaveLines.ViolationCode + OrderNote.MustSaySomething.ViolationCode
            : violations[0].Code + violations[0].Message + violations[0].EntityType?.Name + violations[0].EntityId;
    }

    /// <summary>
    /// DDDToolkit.EntityFramework.Analyzers, and the whole point of this project: the registration is
    /// named from the DDD_Module property, which only reaches the generator through
    /// build/DDDToolkit.props inside the DDDToolkit package. If that file stops shipping or stops
    /// applying, the generator falls back to the assembly name and this line stops compiling.
    /// </summary>
    public static ModelConfigurationBuilder ModuleNamedRegistration(ModelConfigurationBuilder builder)
        => Converters.ConverterExtensions.AddNugetTestConverters(builder);

    /// <summary>DDDToolkit.EntityFramework.Analyzers: the per-identifier value converters.</summary>
    public static void Converters_()
    {
        _ = new CustomerId.CustomerIdConverter();
        _ = new EmailAddress.EmailAddressConverter();
    }
}
