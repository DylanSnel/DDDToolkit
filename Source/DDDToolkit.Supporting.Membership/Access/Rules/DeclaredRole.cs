namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// A role declared in code: a name, and the permissions a member that holds it has on the resource. Of fixed roles
/// (<see cref="Roles.Fixed"/>) a member holds it as a <see cref="NamedRole"/> of the same name.
/// <code>
/// new DeclaredRole("contributor", [DocumentPermissions.View, DocumentPermissions.Edit])
/// </code>
/// <para>
/// Of a customer's roles (<see cref="Roles.Custom"/>) it is a starter role instead: what a first role is made from,
/// with this name and these permissions, and nothing a member holds.
/// </para>
/// </summary>
/// <param name="Name">
/// The role's name, unique among the roles of its rules: 1 to <see cref="NamedRole.MaxLength"/> characters,
/// with no white space around it. It is what a member's row stores, so a name once given out stays.
/// </param>
/// <param name="Permissions">The permissions the role gives on the resource, each once.</param>
public sealed record DeclaredRole(string Name, IReadOnlyList<string> Permissions);
