namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// A role a resource's rules declare: a name, and the keys a member that holds it has on that resource. A
/// member holds it as a <see cref="NamedRole"/> of the same name.
/// <code>
/// new DeclaredRole("contributor", [DocumentKeys.View, DocumentKeys.Edit])
/// </code>
/// <para>
/// Under rules whose roles are kept for the resource (<see cref="MembershipRules.RolesKept"/>) it is a starter
/// role instead: what a first role is made from, with this name and these keys, and nothing a member holds.
/// </para>
/// </summary>
/// <param name="Name">
/// The role's name, unique among the roles of its rules: 1 to <see cref="NamedRole.MaxLength"/> characters,
/// with no white space around it. It is what a member's row stores, so a name once given out stays.
/// </param>
/// <param name="Keys">The permission keys the role gives on the resource, each once.</param>
public sealed record DeclaredRole(string Name, IReadOnlyList<string> Keys);
