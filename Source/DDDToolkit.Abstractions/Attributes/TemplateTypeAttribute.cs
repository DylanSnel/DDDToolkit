namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Fills one type parameter of a <see cref="TemplateRegistrationAttribute"/> method from the application's
/// class declared with a template, the way <see cref="TemplateArgumentAttribute"/> fills one of a parent's:
/// <code>
/// [TemplateType(typeof(SubscriptionAttribute&lt;&gt;), Take = TemplateArgumentKind.Type)] TSubscription,
/// [TemplateType(typeof(SubscriptionAttribute&lt;&gt;))] TSubscriptionId
/// </code>
/// The first takes the application's subscription class itself, the second the id it is declared with.
/// Exactly one class is declared with the template: in the project, or, when the project declares none, in
/// the projects it references. None or several is a diagnostic on the application's project, unless the
/// template allows several (<see cref="EntityTemplateAttribute.AllowSeveral"/>): then each class gets a
/// registration of its own.
/// <para>
/// A template with more than one type argument, such as <c>[Comment&lt;CommentId, AuthorId&gt;]</c>, hands the
/// later ones over with <see cref="Argument"/>:
/// <code>
/// [TemplateType(typeof(CommentAttribute&lt;,&gt;), Argument = 1)] TAuthorId
/// </code>
/// </para>
/// <para>
/// A type parameter of a generic class is filled the same way, for a class the package names with
/// <see cref="TemplateFacadeAttribute"/>: the project that declares the classes gets a class of its own, named as
/// the package's class is without its type parameters, that derives from it closed over them, so no project writes
/// its type arguments. There every type parameter carries one, since that class leaves none open.
/// </para>
/// </summary>
/// <param name="template">The open generic template attribute, such as <c>typeof(SubscriptionAttribute&lt;&gt;)</c>.</param>
[AttributeUsage(AttributeTargets.GenericParameter, Inherited = false)]
public sealed class TemplateTypeAttribute(Type template) : Attribute
{
    /// <summary>The open generic template attribute whose class fills the type parameter.</summary>
    public Type Template { get; } = template;

    /// <summary>What of that class fills the type parameter: its id (the default), or the class itself.</summary>
    public TemplateArgumentKind Take { get; set; } = TemplateArgumentKind.Id;

    /// <summary>
    /// Which type argument of the template attribute, as the class is declared with it, fills the type
    /// parameter: zero-based, and 0 by default, which is the id. A method that takes the class and leaves a
    /// later type argument of its template open cannot be closed, because the class is only what its parent
    /// asks over the very types it was declared with; so a registration for a template with several type
    /// arguments takes each of them.
    /// <para>
    /// Only the id has to be an entity id. A later type argument is whatever the application wrote there, and
    /// one that does not meet the method's constraints is reported (DDD00050). It goes with
    /// <see cref="TemplateArgumentKind.Id"/> only: together with <see cref="TemplateArgumentKind.Type"/>, or
    /// beyond the template's type parameters, the method is not one the generator can close.
    /// </para>
    /// </summary>
    public int Argument { get; set; }

    /// <summary>
    /// With a later <see cref="Argument"/> that names one of the application's entities or aggregate roots:
    /// takes the id that class is declared with rather than the class itself. A template that names the
    /// aggregate its class belongs to, <c>[Comment&lt;SongCommentId, Song&gt;]</c>, hands a registration both:
    /// <code>
    /// [TemplateType(typeof(CommentAttribute&lt;,&gt;), Argument = 1)] TTopic,
    /// [TemplateType(typeof(CommentAttribute&lt;,&gt;), Argument = 1, IdOfArgument = true)] TTopicId
    /// </code>
    /// so the application writes the class once and neither the class nor its id again where it registers.
    /// <para>
    /// The id is read from how the class is declared, <c>[AggregateRoot&lt;SongId&gt;]</c>, <c>[Entity&lt;TId&gt;]</c>
    /// or a template, in the project or in one it references. A type argument that is not declared an entity
    /// or an aggregate root has no id to take, and is reported (DDD00050). Without a later
    /// <see cref="Argument"/> it says nothing: the first type argument is the id already.
    /// </para>
    /// </summary>
    public bool IdOfArgument { get; set; }
}
