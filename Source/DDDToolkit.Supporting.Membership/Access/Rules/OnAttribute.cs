using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The level of a permission that is asked on one resource with members, <typeparamref name="TResource"/>, and how
/// it is held there: by the resource's owner, by every member, through a role. A constant of a
/// <see cref="PermissionsAttribute"/> class carries it.
/// <code>
/// [Permissions]
/// public static class BoardPermissions
/// {
///     /// &lt;summary&gt;See a board and who is on it.&lt;/summary&gt;
///     [On&lt;Board&gt;(EveryMember = true)]
///     public const string View = "boards.view";
///
///     /// &lt;summary&gt;Change a board.&lt;/summary&gt;
///     [On&lt;Board&gt;]
///     public const string Edit = "boards.edit";
///
///     /// &lt;summary&gt;Say who is on a board, and in which role.&lt;/summary&gt;
///     [On&lt;Board&gt;(InRoles = false, LocksMembers = true, LocksOwner = true)]
///     public const string ManageMembers = "boards.members.manage";
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The package's generator collects every permission on a resource into its rules, <c>MembershipRules.For&lt;Board&gt;</c>,
/// in the project that makes them, from the classes of that project and of the projects it references. So the C#
/// check in front of a handler and the functions in the database answer from the same declaration. The defaults
/// are what most permissions of a resource are: the owner holds it, a role can give it, nobody holds it by being a
/// member alone, and it locks nothing.
/// </para>
/// <para>
/// <typeparamref name="TResource"/> is the aggregate root a member class names, <c>[Member&lt;..., Board&gt;]</c>; another
/// class is DDD00093. Of the permissions on one resource, one at most is held by every member, one at most locks
/// the members, and one at most locks the owner (DDD00093).
/// </para>
/// </remarks>
/// <typeparam name="TResource">The resource the permission is asked on.</typeparam>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class OnAttribute<TResource> : PermissionLevelAttribute
    where TResource : class
{
    /// <summary>
    /// Whether the resource's owner holds the permission by owning it: the member its <c>[Owner]</c> property names,
    /// with no role, from the moment the resource names it and for as long as it does. <see langword="true"/> unless
    /// said.
    /// <para>
    /// <c>Owner = false</c>: the owner holds it only as anybody would, through a role it is given or from above; a
    /// permission the owner is never meant to hold by owning, such as one held from above alone.
    /// </para>
    /// <para>
    /// The owner holds nothing through a role: what an owner may do is exactly the permissions said here with
    /// <c>Owner = true</c>. So a request that requires such a permission on the resource lets the owner through
    /// already, and <c>[OrOwn]</c> beside it says nothing more (DDD00096).
    /// </para>
    /// </summary>
    public bool Owner { get; set; } = true;

    /// <summary>
    /// Whether every member holds the permission by being a member, whatever roles it holds, or none. Usually the
    /// permission that sees the resource. <see langword="false"/> unless said.
    /// <para>
    /// <c>EveryMember = true</c>: being put on a resource is enough to hold it, for as long as the membership runs.
    /// Where the rules let a resource be reached from above it is also the permission that sees it from there: a
    /// caller that holds it where the resource sits sees the resource. One permission of a resource at most.
    /// </para>
    /// </summary>
    public bool EveryMember { get; set; }

    /// <summary>
    /// Whether a role can give the permission to a member. <see langword="true"/> unless said.
    /// <para>
    /// <c>InRoles = false</c>: no role can contain this permission, so only the owner (and reach from above) holds it.
    /// A role declared in code that lists it is refused when the rules are made, and a role a customer makes that
    /// lists it is refused with <c>permission-not-for-members</c>; a role that holds it from before the rules changed gives
    /// nothing with it. Say it of a permission a customer must never hand out, such as the one that says who is on
    /// the resource.
    /// </para>
    /// </summary>
    public bool InRoles { get; set; } = true;

    /// <summary>
    /// Whether this is the permission the application's commands require to add or remove a member and to give or
    /// take a role, so a database that checks every row itself holds the member rows to it. <see langword="false"/>
    /// unless said; one permission of a resource at most.
    /// <para>
    /// <c>LocksMembers = true</c>: whatever writes the database's policies adds a lock to both member tables, and a
    /// member row is written only by a caller that holds this permission on the resource, or the one that locks the
    /// owner. Without it, whoever may change a resource in any way writes its member rows, a role for itself
    /// included, by a statement that never went through a command. No code of the package gates a command by it:
    /// the commands still say what they require.
    /// </para>
    /// </summary>
    public bool LocksMembers { get; set; }

    /// <summary>
    /// Whether this is the permission the application's command requires to hand the resource to another owner, so
    /// a database that checks every row itself holds the owner column to it. <see langword="false"/> unless said;
    /// one permission of a resource at most.
    /// <para>
    /// <c>LocksOwner = true</c>: whatever writes the database's policies pins the owner column, and a change of a
    /// resource leaves its owner as it was unless the caller holds this permission on it. The owner holds what it
    /// holds by owning, so whoever may write that column could make itself everything.
    /// </para>
    /// </summary>
    public bool LocksOwner { get; set; }

    /// <summary>
    /// Whether the permission reaches the resource from above, where its rules let a resource be reached so: held
    /// where the resource sits, or above it. <see langword="true"/> unless said.
    /// <para>
    /// <c>FromAbove = false</c>: holding it where the resource sits gives nothing on the resource; it is held there
    /// by the owner, every member or a role alone. Not on the permission every member holds, which is the one that
    /// sees the resource from above.
    /// </para>
    /// </summary>
    public bool FromAbove { get; set; } = true;
}
