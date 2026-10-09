using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// One <c>[RowAccess&lt;TAggregate&gt;]</c> rule as a policy is written from it: its name, the aggregate it
/// guards, what it allows, for which roles, and the SQL the generator translated its <c>Allows</c> method
/// into. A column rule, made with <see cref="ForColumns(string, string, IReadOnlyList{string}, string, string[])"/>,
/// also names the properties whose change it holds, and is written as a trigger rather than a policy.
/// <para>
/// You rarely build one yourself. <c>DDDToolkit.EntityFramework.Supabase</c>'s build step writes one per
/// rule it finds in the modules a host references, and exports them as policies. Build one by hand, from
/// the rule's generated <c>RowAccessSql</c> constant, to hand to
/// <see cref="PostgresRowAccess.Script(Microsoft.EntityFrameworkCore.DbContext, IEnumerable{RowAccessRule}, IEnumerable{RowAccessFunction}?, RowAccessExport)">PostgresRowAccess.Script</see>
/// from a migration, a test or a tool of your own. A column rule's constant starts with <c>{columns}</c>:
/// <see cref="ForColumns(string, string, IReadOnlyList{string}, string, string[])"/> takes it, and
/// <see cref="For(string, string, RowOperations, string, string[])"/> refuses it, since as a rule about whole
/// rows it would let whoever it allows change every column.
/// </para>
/// </summary>
public sealed class RowAccessRule
{
    /// <summary>
    /// What the generator writes at the start of a column rule's SQL. An export of a version that knows no column
    /// rules stops at it, as at any placeholder it does not know, rather than write a policy for the whole row.
    /// </summary>
    internal const string ColumnRuleMarker = "{columns}";

    private RowAccessRule(string name, string aggregateTypeName, RowOperations operations, string sql, IReadOnlyList<string> roles, IReadOnlyList<string> columns)
    {
        Name = name;
        AggregateTypeName = aggregateTypeName;
        Operations = operations;
        Sql = sql;
        Roles = roles;
        Columns = columns;
    }

    /// <summary>The policy's name, as Postgres lists it: <c>A customer sees their orders</c>.</summary>
    public string Name { get; }

    /// <summary>The aggregate root's CLR type name, as <see cref="Type.FullName"/> spells it.</summary>
    public string AggregateTypeName { get; }

    /// <summary>What the rule lets a caller do with a row it allows.</summary>
    public RowOperations Operations { get; }

    /// <summary>The SQL template the generator wrote: <c>{col:...}</c>, <c>{val:...}</c> and <c>{caller:...}</c> still to fill in.</summary>
    public string Sql { get; }

    /// <summary>
    /// The roles the rule is for, each getting policies of its own: <c>RowAccessRoles.User</c>,
    /// <c>Anonymous</c> and <c>SystemIn</c>, which a script writes as the roles its
    /// <see cref="RowAccessExport"/> names, or a role's own name, written as it is spelled. Empty means
    /// <c>RowAccessRoles.User</c> and <c>RowAccessRoles.Anonymous</c>. A script refuses a rule for
    /// <c>PUBLIC</c>.
    /// </summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// The properties of the aggregate whose change a column rule holds, as <c>Columns</c> of its attribute names
    /// them: <c>State</c>, a value object stored in the row, <c>Planned</c>, or one of its properties,
    /// <c>Planned.From</c>. Empty for a rule about whole rows, which is every rule but one made with
    /// <see cref="ForColumns(string, string, IReadOnlyList{string}, string, string[])"/>.
    /// </summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Whether this is a column rule: a trigger on a change of <see cref="Columns"/>, and no policy.</summary>
    public bool IsColumnRule => Columns.Count > 0;

    /// <summary>
    /// A rule on <typeparamref name="TAggregate"/>, for <paramref name="roles"/>: symbolic ones, such as
    /// <c>RowAccessRoles.User</c>, or roles' own names; none for <c>RowAccessRoles.User</c> and
    /// <c>RowAccessRoles.Anonymous</c>. See <see cref="Roles"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A name or the SQL is empty, or no operation is given.</exception>
    public static RowAccessRule For<TAggregate>(string name, RowOperations operations, string sql, params string[] roles)
        => For(typeof(TAggregate).FullName!, name, operations, sql, roles);

    /// <summary>
    /// A rule on the aggregate named <paramref name="aggregateTypeName"/>, which is how generated code names
    /// it, so a rule works whether or not the host can see the aggregate's type. <paramref name="roles"/> are
    /// as for <see cref="For{TAggregate}"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A name or the SQL is empty, no operation is given, or the SQL is a column rule's, which
    /// <see cref="ForColumns(string, string, IReadOnlyList{string}, string, string[])"/> makes.
    /// </exception>
    public static RowAccessRule For(string aggregateTypeName, string name, RowOperations operations, string sql, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(roles);

        if ((operations & RowOperations.All) == 0)
        {
            throw new ArgumentException($"The rule '{name}' allows no operation. Give it Read, Create, Change, Remove or a combination.", nameof(operations));
        }

        if (sql.StartsWith(ColumnRuleMarker, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The SQL of '{name}' is a column rule's, which holds a change of some columns alone: as a rule about whole rows it would let whoever it allows change every column. Make it with RowAccessRule.ForColumns, naming the columns its attribute names.",
                nameof(sql));
        }

        return new(name, aggregateTypeName, operations & RowOperations.All, sql, Named(roles), []);
    }

    /// <summary>
    /// A column rule on <typeparamref name="TAggregate"/>: a change of <paramref name="columns"/>, properties of
    /// the aggregate, is allowed to <paramref name="roles"/> only where the rule allows the row as it was and as
    /// it is about to be. See <see cref="ForColumns(string, string, IReadOnlyList{string}, string, string[])"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A name or the SQL is empty, or no column is named, or an empty one.</exception>
    public static RowAccessRule ForColumns<TAggregate>(string name, IReadOnlyList<string> columns, string sql, params string[] roles)
        => ForColumns(typeof(TAggregate).FullName!, name, columns, sql, roles);

    /// <summary>
    /// A column rule on the aggregate named <paramref name="aggregateTypeName"/>, which is how generated code
    /// names it: a change of <paramref name="columns"/> is allowed only where the rule allows the row as it was
    /// and as it is about to be. It is a rule for <see cref="RowOperations.Change"/>, and a script writes it as a
    /// trigger on those columns, beside the policies, which still decide whether the caller changes the row at
    /// all. <paramref name="roles"/> are as for <see cref="For{TAggregate}"/>.
    /// </summary>
    /// <param name="aggregateTypeName">The aggregate root's CLR type name, as <see cref="Type.FullName"/> spells it.</param>
    /// <param name="name">The rule's name, which the trigger's comment and its refusal name.</param>
    /// <param name="columns">
    /// The properties whose change the rule holds: <c>State</c>, a value object stored in the aggregate's row,
    /// which is every column it is stored in, or <c>Planned.From</c>, one of them.
    /// </param>
    /// <param name="sql">
    /// The SQL template the generator wrote into the rule, which starts with <c>{columns}</c>, or a template of
    /// your own without it.
    /// </param>
    /// <param name="roles">The roles the rule is for.</param>
    /// <exception cref="ArgumentException">A name or the SQL is empty, or no column is named, or an empty one.</exception>
    public static RowAccessRule ForColumns(string aggregateTypeName, string name, IReadOnlyList<string> columns, string sql, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(roles);

        var template = sql.StartsWith(ColumnRuleMarker, StringComparison.Ordinal) ? sql[ColumnRuleMarker.Length..] : sql;
        ArgumentException.ThrowIfNullOrWhiteSpace(template, nameof(sql));

        if (columns.Count == 0 || columns.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"The column rule '{name}' names {(columns.Count == 0 ? "no column" : "an empty column")}. Name the properties whose change it holds, such as nameof(Project.State).", nameof(columns));
        }

        return new(name, aggregateTypeName, RowOperations.Change, template, Named(roles), [.. columns.Select(column => column.Trim()).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>The roles that are named, each once.</summary>
    private static string[] Named(string[] roles) => [.. roles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal)];

    /// <inheritdoc />
    public override string ToString() => IsColumnRule ? $"{Name} ({AggregateTypeName}: {string.Join(", ", Columns)})" : $"{Name} ({AggregateTypeName})";
}
