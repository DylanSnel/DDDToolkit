namespace DDDToolkit.Messaging.Postgres;

/// <summary>
/// The database this was pointed at does not have the pgmq extension, so there are no queues and no
/// <c>pgmq.send</c> to call.
/// <para>
/// It exists so the failure says what is wrong instead of surfacing <c>schema "pgmq" does not exist</c>
/// or <c>function pgmq.send(...) does not exist</c>, which look like a bug in this package and are not.
/// </para>
/// </summary>
public sealed class PgmqNotInstalledException : InvalidOperationException
{
    /// <summary>Says which database is missing the extension, and how to add it.</summary>
    /// <param name="database">The database that was checked.</param>
    /// <param name="inner">The Postgres error that gave it away, when there was one.</param>
    public PgmqNotInstalledException(string? database, Exception? inner = null)
        : base(Describe(database), inner)
        => Database = database;

    /// <summary>The database that has no pgmq.</summary>
    public string? Database { get; }

    private static string Describe(string? database)
    {
        var where = string.IsNullOrEmpty(database) ? "this database" : $"database '{database}'";

        return $"The pgmq extension is not installed in {where}, so there is nothing to enqueue to. " +
            $"Install it once, as a user that may create extensions: CREATE EXTENSION IF NOT EXISTS pgmq; " +
            "The extension itself has to be present on the server first. Postgres images that ship it include " +
            "ghcr.io/pgmq/pg17-pgmq, and managed Postgres that exposes queues (Supabase Queues, for one) already has it; " +
            "see https://github.com/pgmq/pgmq for the other ways to install it.";
    }
}
