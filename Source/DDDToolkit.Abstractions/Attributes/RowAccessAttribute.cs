namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a rule about who may do what with the rows of <typeparamref name="TAggregate"/>, written as a C#
/// expression and turned into a row level security policy the database enforces for every query. Apply to
/// a <c>static partial class</c> with one method, whose body is a single expression:
/// <code>
/// [RowAccess&lt;Order&gt;(RowOperations.Read | RowOperations.Change)]
/// public static partial class ACustomerSeesTheirOrders
/// {
///     public static bool Allows(Order order, ICurrentUser user)
///         => order.PlacedBy == null || order.PlacedBy?.Value == user.Id;
/// }
/// </code>
/// The generator translates <c>Allows</c> into SQL when the class compiles, and reports what it cannot
/// translate as an error on that expression. <c>DDDToolkit.EntityFramework.Postgres</c> turns the rule into
/// Postgres policies, for the aggregate's table and every table of its entities, and
/// <c>DDDToolkit.EntityFramework.Supabase</c> writes those into <c>supabase/migrations</c> as part of the
/// build. The method stays an ordinary method, so a handler can ask the same rule in C#. A rule that names
/// <see cref="Columns"/> holds a change of those columns alone, and becomes a trigger rather than a policy.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root the rule guards. Its entities follow it.</typeparam>
/// <param name="operations">What the rule lets a caller do with a row it allows.</param>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
#pragma warning disable CS9113 // Parameter is unread (read by the source generator).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RowAccessAttribute<TAggregate>(RowOperations operations) : Attribute
#pragma warning restore CS9113
#pragma warning restore S2326
{
    /// <summary>
    /// The roles the rule is for, each getting policies of its own: <see cref="RowAccessRoles.User"/>,
    /// <see cref="RowAccessRoles.Anonymous"/>, <see cref="RowAccessRoles.SystemIn"/> and a token role the host
    /// mapped (<see cref="RowAccessRoles.TokenPrefix"/> and its name), which become the
    /// roles the host configured when the policies are written, or a role's own name, written as it is
    /// spelled. Left empty, it is for <see cref="RowAccessRoles.User"/> and
    /// <see cref="RowAccessRoles.Anonymous"/>: on Supabase, <c>authenticated</c> and <c>anon</c>.
    /// <c>PUBLIC</c> is refused when the policies are written.
    /// </summary>
    public string[] To { get; set; } = [];

    /// <summary>
    /// The properties of <typeparamref name="TAggregate"/> whose change the rule holds, which makes it a column
    /// rule: <c>Columns = [nameof(Project.State)]</c>. A row's own rules let a caller change a row, whichever of
    /// its columns the change is to; a column rule is one condition more, for a change of these columns alone,
    /// where one of them takes a stricter permission than the rest of the row. It goes with
    /// <see cref="RowOperations.Change"/> and no other operation. A value object stored in the aggregate's row
    /// is every column it is stored in, and <c>"Planned.From"</c> is one of them. Left empty, the rule is about
    /// whole rows, as it always was.
    /// </summary>
    /// <remarks>
    /// A policy cannot see which column a statement changes, so the export writes a trigger, before an update of
    /// those columns, that asks <c>Allows</c> of the row as it was and as it is about to be, as a policy for
    /// <c>UPDATE</c> asks a rule, and refuses the statement when either answer is no, as the toolkit's access
    /// guards refuse: <c>42501</c> with its hint, so a save through Entity Framework is refused with
    /// <c>access.refused</c>, as one a policy refuses is. The row's own policy for <c>UPDATE</c> still applies
    /// first. Several column rules on one column add up: a change one of them allows is allowed. A caller's role
    /// that none of them is for may not change the column. The application's own work, the scoped system role
    /// and the bookkeeping role, and the tables' owner are not held, unless a column rule names the role in
    /// <see cref="To"/>.
    /// </remarks>
    public string[] Columns { get; set; } = [];

    /// <summary>
    /// Whether the rule narrows what other rules and the packages allow, rather than allowing more: a
    /// <c>RESTRICTIVE</c> policy, as Postgres and its tools call it. Left <see langword="false"/>, a rule is a
    /// permissive policy, and permissive policies combine with OR: each says "this is also allowed", so a row one of
    /// them allows is allowed. A restrictive rule combines with AND: it says "only if", and a row passes only where it
    /// and a permissive policy both say yes. It never grants anything a permissive policy does not.
    /// <code>
    /// [RowAccess&lt;Organization&gt;(RowOperations.Read, Restrictive = true)]
    /// public static partial class OnlyEmployeesReadTheOrganization
    /// {
    ///     public static bool Allows(Organization organization, ICurrentUser user)
    ///         =&gt; TenancyRowAccess.CurrentSeat&lt;Seat&gt;().Kind == SeatKind.Employee;
    /// }
    /// </code>
    /// So a rule written without it, about a table where a package's default already lets every member read, has no
    /// effect: the default already says yes. A restrictive rule is written for each operation it names, on the
    /// aggregate's table and the tables of its entities: <see cref="RowOperations.Read"/> narrows what a caller reads,
    /// <see cref="RowOperations.Create"/>, <see cref="RowOperations.Change"/> and <see cref="RowOperations.Remove"/>
    /// what it writes. It goes with no <see cref="Columns"/>, and it holds back the roles it is for, those of
    /// <see cref="To"/>, and nobody else: a package's own work and its functions that run as their owner keep
    /// working.
    /// </summary>
    public bool Restrictive { get; set; }
}

/// <summary>What a row access rule lets a caller do with the rows it allows.</summary>
[Flags]
public enum RowOperations
{
    /// <summary>Read the row: <c>SELECT</c>.</summary>
    Read = 1,

    /// <summary>Add a row the rule allows: <c>INSERT</c>, checked against the new row.</summary>
    Create = 2,

    /// <summary>
    /// Change a row the rule allows, into one it still allows: <c>UPDATE</c>. With
    /// <see cref="RowAccessAttribute{TAggregate}.Columns"/>, a change of those columns of such a row.
    /// </summary>
    Change = 4,

    /// <summary>Remove the row: <c>DELETE</c>.</summary>
    Remove = 8,

    /// <summary>All four.</summary>
    All = Read | Create | Change | Remove,
}
