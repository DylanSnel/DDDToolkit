namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>What a database refused, as far as the toolkit reads a failed save.</summary>
public enum DatabaseRefusalKind
{
    /// <summary>A row would have repeated the values of a unique index or a unique constraint.</summary>
    DuplicateKey,

    /// <summary>A row level security policy denied the row a statement would have written.</summary>
    PolicyDenied,

    /// <summary>
    /// An access guard of the database refused the statement: a trigger, or a function a statement ran, that
    /// raised SQLSTATE <c>42501</c> with <see cref="DatabaseRefusal.GuardHint"/> as its hint, as the toolkit's
    /// own access guards do. <see cref="DatabaseRefusal.Constraint"/> is the name the guard gave itself.
    /// </summary>
    GuardRefused,
}

/// <summary>
/// A database's failure of a save, as far as the toolkit reads it: which of the things it knows was refused,
/// and what the database named. <see cref="From"/> reads it from the exception a save threw, for
/// <see cref="DatabaseRefusalInterceptor"/> and for a translation of your own.
/// </summary>
/// <param name="Kind">What was refused.</param>
/// <param name="Constraint">
/// The unique index or constraint the database named, by its name in the database, or the name a guard gave
/// itself; <see langword="null"/> when it named none, as SQLite does for an index over columns and Postgres
/// does for a policy.
/// </param>
/// <param name="Table">The table the database named, without its schema, or <see langword="null"/>.</param>
/// <param name="Columns">
/// The columns the database named, in the order it named them: SQLite's for a unique index. Empty when it named
/// none.
/// </param>
public sealed record DatabaseRefusal(DatabaseRefusalKind Kind, string? Constraint, string? Table, IReadOnlyList<string> Columns)
{
    /// <summary>
    /// The hint an access guard of the database raises its refusal with, beside SQLSTATE <c>42501</c>
    /// (<c>insufficient_privilege</c>): <c>ddd:access.refused</c>. A save the guard refuses is then refused with
    /// <c>access.refused</c>, as one a policy refuses is.
    /// <code>
    /// RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_owner_stays',
    ///     HINT = 'ddd:access.refused', MESSAGE = 'The owner is changed by a caller that holds projects.owner.change.';
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A guard needs a mark because nothing else tells its refusal apart: every <c>RAISE</c> of PL/pgSQL comes
    /// from the same routine of Postgres, and a missing privilege, which is the application's own set-up and no
    /// refusal, is <c>42501</c> as well. The hint of a <c>RAISE</c> is the trigger's own text: Postgres passes it
    /// on as written and never translates it, and it never gives an error of its own this hint, though it gives
    /// some of its own errors hints of their own, in the server's language. So the mark means the same whatever
    /// language the server answers in, and it is read only when it is exactly this. The constraint is the
    /// guard's name, which the warning of a refused save names.
    /// </para>
    /// <para>
    /// The code stays <c>42501</c>, because that is what every tool already reads as "not allowed": the Data
    /// API answers it with a 403, and a pgTAP test that expects a refusal expects it. A code of the toolkit's
    /// own would make those a failure of another kind. <c>RowAccessModel.Refusal</c> in
    /// <c>DDDToolkit.EntityFramework.Postgres</c> writes the statement for a guard written from C#.
    /// </para>
    /// </remarks>
    public const string GuardHint = GuardMark.Hint;

    /// <summary>The schema of <see cref="Table"/>, where the database named it: Postgres and SQL Server do for a unique index.</summary>
    public string? Schema { get; init; }

    /// <summary>
    /// What the database refused in <paramref name="failure"/>, the exception a save threw, or
    /// <see langword="null"/> when it is nothing the toolkit reads. The innermost database exception is read, by
    /// the names of the provider's own exception type, so no provider package is needed here:
    /// <list type="bullet">
    /// <item><b>Npgsql.</b> SQLSTATE <c>23505</c> is a duplicate key, with the index, the table and the schema
    /// Postgres names. SQLSTATE <c>42501</c> with the hint <see cref="GuardHint"/> is a guard's refusal, with
    /// the name the guard gave itself as the constraint, and the table and the schema where it named them.
    /// SQLSTATE <c>42501</c> raised by Postgres's check of a new row against the policies is a policy's denial:
    /// the error names that routine, <c>ExecWithCheckOptions</c>, whatever language the server answers in,
    /// and the table is the one the message quotes. Any other <c>42501</c> is not a refusal: a missing
    /// privilege, or a role the policies hold that reads with <c>row_security</c> off, is the application's
    /// own set-up, and a trigger that leaves out the hint did not ask to be answered as a refusal.</item>
    /// <item><b>SQLite.</b> Extended error <c>2067</c> is a duplicate key, with the table and the columns of
    /// the message, or the index's name for an index over expressions.</item>
    /// <item><b>SQL Server.</b> Errors <c>2601</c> and <c>2627</c> are a duplicate key, with the index or
    /// constraint and the table as the message quotes them; a server that words its messages in another
    /// language names neither.</item>
    /// </list>
    /// </summary>
    /// <param name="failure">
    /// The exception a save threw, usually a <c>DbUpdateException</c>, or the database's own exception a statement
    /// of yours threw, an <c>ExecuteUpdate</c> say, which no interceptor of a save translates.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is null.</exception>
    public static DatabaseRefusal? From(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return DatabaseRefusals.Read(failure);
    }
}
