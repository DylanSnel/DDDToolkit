namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Turns a generic assembly attribute a package declares into the line that tells the generator which of a project's
/// ids are the ids of the package's classes, for a module that sees those ids and none of the classes. Apply it to the
/// attribute class, and give each of its type parameters the template whose class's id it is:
/// <code>
/// [TemplateIds]
/// [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
/// public sealed class BillingIdsAttribute&lt;
///     [TemplateType(typeof(SubscriptionAttribute&lt;&gt;))] TSubscriptionId,
///     [TemplateType(typeof(InvoiceLineAttribute&lt;&gt;))] TInvoiceLineId&gt; : Attribute
///     where TSubscriptionId : struct, IEntityId, IEquatable&lt;TSubscriptionId&gt;
///     where TInvoiceLineId : struct, IEntityId, IEquatable&lt;TInvoiceLineId&gt;;
/// </code>
/// A project of another module writes the line once, with the ids it sees in the contracts of the module that declares
/// the classes:
/// <code>
/// [assembly: BillingIds&lt;SubscriptionId, InvoiceLineId&gt;]
/// </code>
/// <para>
/// <b>What the line gives.</b> Everything the package has closed over those ids alone. A registration whose
/// <see cref="TemplateTypeAttribute"/>s take only ids gets its wrapper, as in the project that declares the classes:
/// in the project of the module that holds what the registration is for, the infrastructure project say, below the
/// one with the line. A class the package asks for with <see cref="TemplateFacadeAttribute"/>, whose type parameters
/// take only ids, is written into the project with the line, in its root namespace. A registration or a class that
/// takes a class rather than an id gets nothing here: those are the declaring module's.
/// </para>
/// <para>
/// <b>Where.</b> In one project of the module, usually its application project, where the code that names the
/// package's classes is. The project below it of the same module that holds the context reads the line there. A
/// class or an id the project or its module declares with a template wins over the line.
/// </para>
/// <para>
/// Each type parameter of the attribute class takes the id of one template, as <see cref="TemplateTypeAttribute"/>
/// says it, with neither <see cref="TemplateTypeAttribute.Take"/> nor <see cref="TemplateTypeAttribute.Argument"/>.
/// Constrain each one as the package's classes constrain the id, so the compiler checks the line. A type parameter
/// that names no template is the package's mistake, and the line gives nothing.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemplateIdsAttribute : Attribute;
