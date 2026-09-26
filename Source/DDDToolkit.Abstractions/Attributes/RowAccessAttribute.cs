namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a rule about who may do what with the rows of <typeparamref name="TAggregate"/>, written as a C#
/// expression and turned into a row level security policy the database enforces for every query. Apply to
/// a <c>static partial class</c> with one method, whose body is a single expression:
/// <code>
/// [RowAccess&lt;Order&gt;(RowOperations.Read | RowOperations.Change)]
/// public static partial class ACustomerSeesTheirOrders
/// {
///     public static bool Allows(Order order, Caller caller)
///         => order.PlacedBy == null || order.PlacedBy?.Value == caller.UserId;
/// }
/// </code>
/// The generator translates <c>Allows</c> into SQL when the class compiles, and reports what it cannot
/// translate as an error on that expression. <c>DDDToolkit.EntityFramework.Postgres</c> turns the rule into
/// Postgres policies, for the aggregate's table and every table of its entities, and
/// <c>DDDToolkit.EntityFramework.Supabase</c> writes those into <c>supabase/migrations</c> as part of the
/// build. The method stays an ordinary method, so a handler can ask the same rule in C#.
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
    /// The database roles the rule is for. Left empty, it is for every caller the application runs
    /// queries as: on Supabase, <c>anon</c> and <c>authenticated</c>.
    /// </summary>
    public string[] To { get; set; } = [];
}

/// <summary>What a row access rule lets a caller do with the rows it allows.</summary>
[Flags]
public enum RowOperations
{
    /// <summary>Read the row: <c>SELECT</c>.</summary>
    Read = 1,

    /// <summary>Add a row the rule allows: <c>INSERT</c>, checked against the new row.</summary>
    Create = 2,

    /// <summary>Change a row the rule allows, into one it still allows: <c>UPDATE</c>.</summary>
    Change = 4,

    /// <summary>Remove the row: <c>DELETE</c>.</summary>
    Remove = 8,

    /// <summary>All four.</summary>
    All = Read | Create | Change | Remove,
}
