namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Turns an attribute a package declares into a way to declare a child entity that derives from one of
/// the package's parents. It is the entity counterpart of <see cref="AggregateRootTemplateAttribute"/>.
/// <para>
/// The attribute may have more type arguments than its parent takes. Those say something about the class
/// that the parent has no use for, such as the aggregate a comment is written on in
/// <c>[Comment&lt;SongCommentId, Song&gt;]</c>, and only a <see cref="TemplateRegistrationAttribute"/>
/// method reads them, by position (<see cref="TemplateTypeAttribute.Argument"/>).
/// </para>
/// </summary>
/// <param name="parent">The open generic parent, a class marked <see cref="EntityBaseAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityTemplateAttribute(Type parent) : Attribute
{
    /// <summary>The open generic parent a class declared with the attribute derives from.</summary>
    public Type Parent { get; } = parent;

    /// <summary>
    /// Whether an application may declare more than one class with the template in one project, or in the
    /// projects of one module. False by default: most templates stand for something an application has one of,
    /// its tenant or its subscription, and a second class is a mistake the generator names (DDD00045).
    /// <para>
    /// Set it for a template an application declares once per thing of its own, such as the comments on a
    /// song and the comments on a video. A <see cref="TemplateRegistrationAttribute"/> method that takes
    /// its types from such a template is then written once per class, each named after its class:
    /// <c>AddCommentsForSongComment()</c> and <c>AddCommentsForVideoComment()</c>. With one class the
    /// registration keeps the method's own name. A method that says what its registration is called
    /// (<see cref="TemplateRegistrationAttribute.Name"/>) is called that with one class and with several.
    /// </para>
    /// <para>
    /// Leave it off for a template another template takes a type from with a
    /// <see cref="TemplateArgumentAttribute"/>: that parent is closed over the one class there is, and several
    /// stay DDD00045 there whatever this says.
    /// </para>
    /// </summary>
    public bool AllowSeveral { get; set; }
}
