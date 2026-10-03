namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>What a database refused, as far as the toolkit reads a failed save.</summary>
public enum DatabaseRefusalKind
{
    /// <summary>A row would have repeated the values of a unique index or a unique constraint.</summary>
    DuplicateKey,

    /// <summary>A row level security policy denied the row a statement would have written.</summary>
    PolicyDenied,
}

/// <summary>
/// A database's failure of a save, as far as the toolkit reads it: which of the two things it knows was refused,
/// and what the database named. <see cref="From"/> reads it from the exception a save threw, for
/// <see cref="DatabaseRefusalInterceptor"/> and for a translation of your own.
/// </summary>
/// <param name="Kind">What was refused.</param>
/// <param name="Constraint">
/// The unique index or constraint the database named, by its name in the database; <see langword="null"/> when
/// it named none, as SQLite does for an index over columns and Postgres does for a policy.
/// </param>
/// <param name="Table">The table the database named, without its schema, or <see langword="null"/>.</param>
/// <param name="Columns">
/// The columns the database named, in the order it named them: SQLite's for a unique index. Empty when it named
/// none.
/// </param>
public sealed record DatabaseRefusal(DatabaseRefusalKind Kind, string? Constraint, string? Table, IReadOnlyList<string> Columns)
{
    /// <summary>The schema of <see cref="Table"/>, where the database named it: Postgres and SQL Server do for a unique index.</summary>
    public string? Schema { get; init; }

    /// <summary>
    /// What the database refused in <paramref name="failure"/>, the exception a save threw, or
    /// <see langword="null"/> when it is nothing the toolkit reads. The innermost database exception is read, by
    /// the names of the provider's own exception type, so no provider package is needed here:
    /// <list type="bullet">
    /// <item><b>Npgsql.</b> SQLSTATE <c>23505</c> is a duplicate key, with the index, the table and the schema
    /// Postgres names. SQLSTATE <c>42501</c> is a policy's denial when it comes from the check of a new row
    /// against the policies, which Postgres says in the error's routine and, in English, in its message; the
    /// table is the one the message quotes. A missing privilege is <c>42501</c> as well, and is not a
    /// refusal.</item>
    /// <item><b>SQLite.</b> Extended error <c>2067</c> is a duplicate key, with the table and the columns of
    /// the message, or the index's name for an index over expressions.</item>
    /// <item><b>SQL Server.</b> Errors <c>2601</c> and <c>2627</c> are a duplicate key, with the index or
    /// constraint and the table as the message quotes them; a server that words its messages in another
    /// language names neither.</item>
    /// </list>
    /// </summary>
    /// <param name="failure">The exception a save threw, usually a <c>DbUpdateException</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is null.</exception>
    public static DatabaseRefusal? From(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return DatabaseRefusals.Read(failure);
    }
}
