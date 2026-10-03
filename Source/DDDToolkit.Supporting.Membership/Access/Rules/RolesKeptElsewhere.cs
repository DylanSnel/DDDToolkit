namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Says that the roles a member holds on a resource are not declared in its rules but kept elsewhere: rows
/// the application keeps somewhere, with the keys each gives, such as the roles an organization makes for
/// itself. A member then holds such a role by its id, and which roles give a key is asked where they are
/// kept.
/// <code>
/// new MembershipRules("crates", keys: [], rolesKeptElsewhere: new("depot/roles_with_key"),
///     ownerRole: "crate-lead", memberKeys: MemberKeys.AllBut("crates.move"))
/// </code>
/// <para>
/// What such a role gives a member is still cut by the rules: only the keys within
/// <see cref="MembershipRules.MemberKeys"/>, whatever else the role holds where it is kept.
/// </para>
/// <para>
/// In C# the application answers through the ports it registers with the resource: which roles there are and
/// which is the owner's (<see cref="UseCases.IMemberRoles{TResourceId, TRoleId}"/>), and which roles give a key,
/// which the package that stores the resources asks inside its own statement. In a database that answers the
/// questions itself, a function of the application's answers which roles give a key, named here.
/// </para>
/// </summary>
public sealed record RolesKeptElsewhere
{
    /// <summary>Roles that are kept elsewhere.</summary>
    /// <param name="function">
    /// The function that answers the roles that give a key in the database, by its logical name,
    /// <c>owner/name</c>: one that takes the key as text and answers the ids of the roles, as a member's row
    /// stores them. Left out by an application whose database does not answer the questions itself; whatever
    /// writes the resource's functions refuses rules that name none. It is asked by the resource's functions,
    /// which run as their owner, so it reads every row whatever policies its tables have: where the roles
    /// are some customer's, it keeps to the caller's by a condition of its own, never by a policy.
    /// It is never asked without a key.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="function"/> is not a logical name, <c>owner/name</c>.</exception>
    public RolesKeptElsewhere(string? function = null) => Function = MemberSource.LogicalName(function, nameof(function));

    /// <summary>
    /// The logical name of the function that answers the roles that give a key in the database, or
    /// <see langword="null"/> when the rules name none.
    /// </summary>
    public string? Function { get; }
}
