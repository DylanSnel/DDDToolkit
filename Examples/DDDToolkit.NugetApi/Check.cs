using DDDToolkit.Interfaces;
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

    /// <summary>DDDToolkit.FluentValidation.Analyzers: the generated Errors collection.</summary>
    public static int ValidationErrors() => EmailAddress.Create("nope").Errors.Count;

    /// <summary>DDDToolkit.Analyzers: the base type, the event list and the invariant seam.</summary>
    public static void AggregateSurface()
    {
        var order = new Order(OrderId.CreateUnique(), CustomerId.CreateUnique());
        order.AddLine(new Sku("ABC"));
        _ = order.Lines.Count;
        _ = order.Version;
        _ = ((IHasDomainEvents)order).DomainEvents;
        order.EnsureInvariants();
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
