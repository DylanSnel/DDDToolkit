using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// A role known by its name: what a member holds when the roles are the ones a resource's rules declare
/// (<see cref="Access.DeclaredRole"/>), rather than rows: the application's role class
/// (<see cref="KeptRoleAttribute{TRoleId, TResource}"/>), or roles it keeps somewhere else.
/// <code>
/// [Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]
/// public sealed partial class DocumentShare;
/// </code>
/// <para>
/// It is an id over the name, so it is stored as that text, in a column of at most <see cref="MaxLength"/>
/// characters, and compares by it. It checks nothing by itself: whether a name is one of a resource's roles is
/// asked where a role is given, of that resource's rules.
/// </para>
/// </summary>
[EntityId<string>(ColumnLength: NamedRole.MaxLength)]
public readonly partial record struct NamedRole
{
    /// <summary>The longest name a role may have.</summary>
    public const int MaxLength = 64;
}
