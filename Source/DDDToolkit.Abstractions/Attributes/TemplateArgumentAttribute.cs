namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Fills one type parameter of a template's parent from another class of the application, so the
/// application does not have to repeat what it already declared. Apply it to an attribute class marked
/// <see cref="AggregateRootTemplateAttribute"/> or <see cref="EntityTemplateAttribute"/>:
/// <code>
/// [AggregateRootTemplate(typeof(InvoiceAggregate&lt;,,,&gt;))]
/// [TemplateArgument(1, typeof(SubscriptionAttribute&lt;&gt;))]
/// [TemplateArgument(2, typeof(InvoiceLineAttribute&lt;&gt;), Take = TemplateArgumentKind.Type)]
/// [TemplateArgument(3, typeof(InvoiceLineAttribute&lt;&gt;))]
/// public sealed class InvoiceAttribute&lt;TInvoiceId&gt; : Attribute;
/// </code>
/// An application that declares <c>[Invoice&lt;InvoiceId&gt;] partial class ShopInvoice</c> then gets
/// <c>ShopInvoice : InvoiceAggregate&lt;InvoiceId, SubscriptionId, ShopInvoiceLine, InvoiceLineId&gt;</c>, taking
/// the subscription's id from its own <c>[Subscription&lt;SubscriptionId&gt;]</c> class and its lines from its
/// <c>[InvoiceLine&lt;InvoiceLineId&gt;]</c> class. Exactly one class is declared with the named attribute: in the
/// project, or, when the project declares none, in the projects it references. None or several is a
/// diagnostic that names the class to add or remove.
/// </summary>
/// <param name="position">The zero-based type parameter of the parent this fills.</param>
/// <param name="source">The open generic template attribute whose one class provides the argument.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class TemplateArgumentAttribute(int position, Type source) : Attribute
{
    /// <summary>The zero-based type parameter of the parent this fills.</summary>
    public int Position { get; } = position;

    /// <summary>The template attribute whose class provides the argument.</summary>
    public Type Source { get; } = source;

    /// <summary>
    /// What of that class fills the parameter: its id (the default), or the class itself, which is what a
    /// parent that holds the class as a child entity needs.
    /// </summary>
    public TemplateArgumentKind Take { get; set; } = TemplateArgumentKind.Id;
}

/// <summary>What a <see cref="TemplateArgumentAttribute"/> takes from the class it finds.</summary>
public enum TemplateArgumentKind
{
    /// <summary>The id the class is declared with.</summary>
    Id,

    /// <summary>The class itself.</summary>
    Type,
}
