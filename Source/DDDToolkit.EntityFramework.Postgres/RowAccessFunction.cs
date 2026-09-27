namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// One <c>[AccessFunction&lt;TAggregate&gt;]</c> as its SQL function is written from it: its name, the aggregate
/// it is about, and the SQL the generator translated its <c>Allows</c> method into.
/// <para>
/// Like <see cref="RowAccessRule"/>, you rarely build one yourself: the Supabase build writes one per access
/// function it finds in the modules a host references. Build one by hand, from the class's generated
/// <c>RowAccessSql</c> constant, to hand to <see cref="PostgresRowAccess.Script"/>.
/// </para>
/// </summary>
public sealed class RowAccessFunction
{
    private RowAccessFunction(string name, string aggregateTypeName, string sql)
    {
        Name = name;
        AggregateTypeName = aggregateTypeName;
        Sql = sql;
    }

    /// <summary>The function's name with its schema: <c>projects.is_member</c>.</summary>
    public string Name { get; }

    /// <summary>The aggregate root's CLR type name, as <see cref="Type.FullName"/> spells it.</summary>
    public string AggregateTypeName { get; }

    /// <summary>The SQL template the generator wrote, with the aggregate's entities in <c>{exists}</c>.</summary>
    public string Sql { get; }

    /// <summary>A function about <typeparamref name="TAggregate"/>.</summary>
    /// <exception cref="ArgumentException">The name is not <c>schema.name</c>, or the SQL is empty.</exception>
    public static RowAccessFunction For<TAggregate>(string name, string sql)
        => For(typeof(TAggregate).FullName!, name, sql);

    /// <summary>A function about the aggregate named <paramref name="aggregateTypeName"/>, which is how generated code names it.</summary>
    /// <exception cref="ArgumentException">The name is not <c>schema.name</c>, or the SQL is empty.</exception>
    public static RowAccessFunction For(string aggregateTypeName, string name, string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var parts = name.Split('.');
        if (parts.Length != 2 || !parts.All(IsIdentifier))
        {
            throw new ArgumentException($"'{name}' is not a function name with its schema, such as 'projects.is_member'.", nameof(name));
        }

        return new(name, aggregateTypeName, sql);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({AggregateTypeName})";

    private static bool IsIdentifier(string part)
        => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.All(static c => char.IsLetterOrDigit(c) || c == '_');
}
