namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a generic registration method a package ships so that every application gets it closed over its
/// own classes, and calls it without type arguments. Apply it to a public static generic extension method
/// whose type parameters carry <see cref="TemplateTypeAttribute"/>:
/// <code>
/// [TemplateRegistration]
/// public static ModelBuilder AddSubscriptions&lt;
///     [TemplateType(typeof(SubscriptionAttribute&lt;&gt;), Take = TemplateArgumentKind.Type)] TSubscription,
///     [TemplateType(typeof(SubscriptionAttribute&lt;&gt;))] TSubscriptionId&gt;(this ModelBuilder modelBuilder)
/// </code>
/// An application that declares <c>[Subscription&lt;SubscriptionId&gt;] partial class ShopSubscription</c> can then
/// write <c>modelBuilder.AddSubscriptions()</c>: the generator writes an internal wrapper into that
/// application, in the method's namespace, that forwards to
/// <c>AddSubscriptions&lt;ShopSubscription, SubscriptionId&gt;(modelBuilder)</c>.
/// <para>
/// The generator finds the method through <see cref="TemplateRegistrationsAttribute"/> on the package's
/// assembly, and writes a wrapper only into a project that declares a class with one of the templates the
/// method names. Each type parameter is filled the way a <see cref="TemplateArgumentAttribute"/> fills a
/// parent's: from the one class declared with its template, in the project or, when the project declares
/// none, in the projects it references. A type parameter without <see cref="TemplateTypeAttribute"/> stays a
/// type parameter of the wrapper, with its constraints, as <c>services.AddSubscriptions&lt;ShopContext&gt;()</c>.
/// </para>
/// <para>
/// A template that may be declared more than once (<see cref="EntityTemplateAttribute.AllowSeveral"/>) gets
/// the wrapper once per class when a project declares several, each named after its class, as
/// <c>modelBuilder.AddCommentsForShopInvoiceComment()</c>. One template of a method may have several classes,
/// not two: there would be no telling which class of the one goes with which of the other.
/// </para>
/// <para>
/// A method says what its wrapper is called with <see cref="Name"/>, when the method's own name is not what an
/// application should call: the wrapper is then called so always, with one class and with several.
/// </para>
/// <para>
/// A module split into projects by layer declares the classes in its domain project, which needs no Entity
/// Framework, and calls the registration where the context is. That project declares none of the classes and
/// gets the wrapper too, when it declares the same <see cref="ModuleAttribute"/>: it is closed over the classes
/// the module's other projects declare, and only those. A project of another module, or of none, gets nothing.
/// </para>
/// </summary>
/// <remarks>
/// The generic method keeps working on its own. The wrapper only saves the application from repeating, in
/// every call, the classes it has already declared once.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class TemplateRegistrationAttribute : Attribute
{
    /// <summary>
    /// What the wrapper is called, when that is not the method's own name: a name in which
    /// <c>{TypeParameter}</c> stands for the name of the type that fills that type parameter of the method.
    /// <code>
    /// [TemplateRegistration(Name = "Add{TTopic}Comments")]
    /// public static ModelBuilder AddComments&lt;
    ///     [TemplateType(typeof(CommentAttribute&lt;,&gt;), Take = TemplateArgumentKind.Type)] TComment,
    ///     [TemplateType(typeof(CommentAttribute&lt;,&gt;))] TCommentId,
    ///     [TemplateType(typeof(CommentAttribute&lt;,&gt;), Argument = 1)] TTopic&gt;(this ModelBuilder modelBuilder)
    /// </code>
    /// An application that declares <c>[Comment&lt;SongCommentId, Song&gt;]</c> then calls
    /// <c>modelBuilder.AddSongComments()</c>, and one that also declares <c>[Comment&lt;VideoCommentId, Video&gt;]</c>
    /// calls <c>modelBuilder.AddVideoComments()</c> beside it: the same name whether the project has one such
    /// class or several, so a second class changes no call that was there.
    /// <para>
    /// Only a type parameter that carries <see cref="TemplateTypeAttribute"/> can be named: the wrapper is
    /// closed over it, so its name is known where the wrapper is written. A type is named as its declaration
    /// names it, without its namespace and its type arguments. Two classes whose wrappers would be called the
    /// same are reported (DDD00045), since a call could not tell them apart, and a type with no name of its
    /// own, an array say, cannot be what a wrapper is named after (DDD00050).
    /// </para>
    /// <para>
    /// Left out, the wrapper has the method's name, and <c>{Method}For{Class}</c> for each class of a template
    /// a project declares several times.
    /// </para>
    /// </summary>
    public string? Name { get; set; }
}
