using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// One <c>[RowAccess&lt;TAggregate&gt;]</c> rule as a policy is written from it: its name, the aggregate it
/// guards, what it allows, for which roles, and the SQL the generator translated its <c>Allows</c> method
/// into.
/// <para>
/// You rarely build one yourself. <c>DDDToolkit.EntityFramework.Supabase</c>'s build step writes one per
/// rule it finds in the modules a host references, and exports them as policies. Build one by hand, from
/// the rule's generated <c>RowAccessSql</c> constant, to hand to <see cref="PostgresRowAccess.Script"/>
/// from a migration, a test or a tool of your own.
/// </para>
/// </summary>
public sealed class RowAccessRule
{
    private RowAccessRule(string name, string aggregateTypeName, RowOperations operations, string sql, IReadOnlyList<string> roles)
    {
        Name = name;
        AggregateTypeName = aggregateTypeName;
        Operations = operations;
        Sql = sql;
        Roles = roles;
    }

    /// <summary>The policy's name, as Postgres lists it: <c>A customer sees their orders</c>.</summary>
    public string Name { get; }

    /// <summary>The aggregate root's CLR type name, as <see cref="Type.FullName"/> spells it.</summary>
    public string AggregateTypeName { get; }

    /// <summary>What the rule lets a caller do with a row it allows.</summary>
    public RowOperations Operations { get; }

    /// <summary>The SQL template the generator wrote: <c>{col:...}</c>, <c>{val:...}</c> and <c>{caller:...}</c> still to fill in.</summary>
    public string Sql { get; }

    /// <summary>The roles the rule is for; empty for every caller the application runs queries as.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>A rule on <typeparamref name="TAggregate"/>.</summary>
    /// <exception cref="ArgumentException">A name or the SQL is empty, or no operation is given.</exception>
    public static RowAccessRule For<TAggregate>(string name, RowOperations operations, string sql, params string[] roles)
        => For(typeof(TAggregate).FullName!, name, operations, sql, roles);

    /// <summary>
    /// A rule on the aggregate named <paramref name="aggregateTypeName"/>, which is how generated code names
    /// it, so a rule works whether or not the host can see the aggregate's type.
    /// </summary>
    /// <exception cref="ArgumentException">A name or the SQL is empty, or no operation is given.</exception>
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

        return new(name, aggregateTypeName, operations & RowOperations.All, sql, [.. roles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal)]);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({AggregateTypeName})";
}
