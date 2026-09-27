namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// SQL inside a row access rule, for what the translation cannot say: a lookup in another table, a
/// function the database already has. The generator writes it into the policy as it is.
/// <code>
/// public static bool Allows(Order order, Caller caller)
///     =&gt; order.PlacedBy?.Value == caller.UserId
///        || Sql.Call&lt;bool&gt;("private.is_support_agent", caller.UserId);
/// </code>
/// </summary>
/// <remarks>
/// A rule with SQL in it is the database's alone: C# cannot run the SQL, so calling <c>Allows</c> throws
/// <see cref="DatabaseOnlyException"/> when it gets to that part. Prefer <see cref="Call{T}"/>: the
/// arguments are translated like the rest of the rule, so a column still comes from the model, and a
/// function marked <c>security definer</c> can read a table the caller may not, without a policy that
/// reads another policy's table. <see cref="Raw{T}"/> is SQL the generator checks nothing of, column
/// names included.
/// </remarks>
public static class Sql
{
    /// <summary>
    /// A call to the SQL function <paramref name="function"/>, <c>schema.name</c>, with the arguments
    /// translated like the rest of the rule: properties of the aggregate, the caller, constants.
    /// </summary>
    /// <typeparam name="T">What the function returns, so the call fits in the rule: <c>bool</c> to use it as a condition.</typeparam>
    /// <param name="function">The function's name, a string written in the rule: letters, digits, <c>_</c> and a <c>.</c> between schema and name.</param>
    /// <param name="arguments">The function's arguments.</param>
    /// <exception cref="DatabaseOnlyException">Always: only the database can run it.</exception>
    public static T Call<T>(string function, params object?[] arguments) => throw new DatabaseOnlyException(function);

    /// <summary>
    /// SQL written into the rule's condition as it is, in parentheses. Column names in it are the ones in the
    /// database, not the properties, and nothing checks them.
    /// </summary>
    /// <typeparam name="T">What the SQL yields, so it fits in the rule: <c>bool</c> for a condition, a column's type to compare with one.</typeparam>
    /// <param name="sql">The SQL, a string written in the rule.</param>
    /// <exception cref="DatabaseOnlyException">Always: only the database can run it.</exception>
    public static T Raw<T>(string sql) => throw new DatabaseOnlyException(sql);
}

/// <summary>
/// A row access rule was asked in C# for an answer only the database can give, because the rule has SQL
/// in it. Ask the database instead: load the row through a context that runs as the caller.
/// </summary>
public sealed class DatabaseOnlyException : InvalidOperationException
{
    /// <summary>The SQL that could not run here.</summary>
    public DatabaseOnlyException(string sql)
        : base($"This row access rule has SQL in it, '{sql}', which only the database can run. Ask the database: load the row through a context that runs as the caller.")
        => SqlText = sql;

    /// <summary>The SQL that could not run here.</summary>
    public string SqlText { get; }
}
