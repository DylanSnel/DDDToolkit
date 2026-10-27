namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Names the Entity Framework convention that maps what the attribute it marks says. A package declares a marker for
/// domain classes, which reference no Entity Framework, and the convention that maps it in its package for Entity
/// Framework; this joins the two, so a context maps the marked classes without a line per class:
/// <code>
/// [AttributeUsage(AttributeTargets.Property)]
/// [ModelConvention("Billing.EntityFramework.BilledToConvention, Billing.EntityFramework")]
/// public sealed class BilledToAttribute : Attribute;
/// </code>
/// <para>
/// <c>AddDDDToolkitConventions()</c> runs the convention in every model that maps a class with the marker, on the
/// class or on one of its properties. It runs once per model, when the model is finalized, and finds the marked
/// classes itself. What a context configures by hand in <c>OnModelCreating</c> is there by then, so the convention
/// can leave a class the context mapped itself as it is.
/// </para>
/// </summary>
/// <remarks>
/// The convention is named by its assembly-qualified name, because the marker's assembly does not reference the
/// convention's. It is a public class with a public constructor that takes nothing, and it implements Entity
/// Framework's <c>IModelFinalizingConvention</c>. A model with a marked class is refused while it is built when the
/// convention's assembly is not in the application, with a message that names the assembly to reference.
/// </remarks>
/// <param name="convention">The convention's assembly-qualified name, as <c>"Namespace.Convention, Assembly"</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModelConventionAttribute(string convention) : Attribute
{
    /// <summary>The convention's assembly-qualified name.</summary>
    public string Convention { get; } = convention;
}
