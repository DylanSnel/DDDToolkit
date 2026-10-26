namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Turns an assembly attribute a package declares into the switch that has the generator write the package's
/// classes an application leaves out, as the package ships them, with their ids. Apply it to the attribute class,
/// naming the templates whose classes it writes:
/// <code>
/// [TemplateDefaults(typeof(SubscriptionAttribute&lt;&gt;), typeof(InvoiceLineAttribute&lt;&gt;))]
/// [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
/// public sealed class GenerateBillingClassesAttribute : Attribute;
/// </code>
/// and an application that wants those classes as they come writes one line instead of a class and an id each:
/// <code>
/// [assembly: GenerateBillingClasses]
/// </code>
/// The project then has a <c>Subscription</c> declared <c>[Subscription&lt;SubscriptionId&gt;]</c> and a
/// <c>SubscriptionId</c> declared <c>[EntityId&lt;Guid&gt;]</c>, public, in its root namespace, with everything the
/// toolkit's generators write for a class and an id the application declared itself: the base class, the
/// converters, the registrations closed over them and the class the package's use cases are named through. A
/// template of another package that names a written id as a later type argument is closed over it as well.
/// Generators that are not the toolkit's, HotChocolate's say, do not see what the switch wrote in the project that
/// has it, only in the projects above it, and code outside the root namespace imports the written types with a
/// <c>global using</c>, which the parts the generators write reach.
/// <para>
/// <b>Nothing out of sight.</b> Nothing is generated without the switch, and a project only gets what it says. The
/// generated declarations say in their documentation that the switch wrote them, and how to replace one.
/// </para>
/// <para>
/// <b>What the application declares wins.</b> A template the project declares a class with, or a project of its
/// module that it references, gets no class: only the missing ones are written. An id is written only when no type
/// of its name can be found, in the project or in a project of its module that it references, and a class takes the
/// one it finds. So an application declares the classes that add fields and keeps the switch for the rest, and a
/// module split by layer writes its ids in its contracts project and its classes in its domain project.
/// </para>
/// <para>
/// <b>Names.</b> A class is called after its template, <c>Subscription</c> for <c>[Subscription]</c> and
/// <c>Tenant</c> for <c>[TenantAggregate]</c>. Its id is the class's name with <c>Id</c> after it, unless the parent
/// names its id after another class, as a parent whose first type parameter is <c>TTenantId</c> does for an
/// organization that shares its tenant's id: then it is that one, <c>TenantId</c>, and the two classes share it. Every
/// id is an <c>[EntityId&lt;Guid&gt;]</c> without a prefix, published with <c>[ModuleContract]</c>, since an id is what
/// other modules store.
/// </para>
/// <para>
/// Only a template whose attribute takes the id alone can be written this way: the generator does not know what a
/// later type argument should be. A type this attribute names that the generator cannot use is passed over without
/// a word: that is the package's mistake, and its own tests show it.
/// </para>
/// </summary>
/// <param name="templates">The open template attributes, each marked <see cref="AggregateRootTemplateAttribute"/> or <see cref="EntityTemplateAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemplateDefaultsAttribute(params Type[] templates) : Attribute
{
    /// <summary>The templates whose classes the switch writes, and whose ids.</summary>
    public IReadOnlyList<Type> Templates { get; } = templates;

    /// <summary>
    /// Whether the switch writes the ids alone: for a module's contracts project, which publishes the ids and holds
    /// no aggregates. False by default, when it writes the classes and every id of theirs that no project declares.
    /// </summary>
    public bool IdsOnly { get; set; }
}
