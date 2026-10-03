namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// How long what <see cref="PostgresRowLevelSecurityInterceptor"/> sets for a caller lasts on the server: as long
/// as the connection is open, or as long as one transaction. Which one is right follows from what is between the
/// application and Postgres, so it is said, never guessed from a host name:
/// <see cref="PostgresRowLevelSecurityOptions.Scope"/>.
/// </summary>
public enum RowLevelSecurityScope
{
    /// <summary>
    /// The role, the claims and the settings are set each time a context opens a connection, for the session,
    /// and the reset Npgsql does before a pooled connection is used again clears them. Right for a direct
    /// connection and for a pooler that keeps one server connection per client connection, such as Supabase's
    /// session pooler. The default.
    /// </summary>
    Connection,

    /// <summary>
    /// The role, the claims and the settings are set for one transaction at a time and end with it, so nothing
    /// of a caller ever lives on a session. Right for a pooler that hands each transaction whichever server
    /// connection is free, such as PgBouncer in transaction mode or Supabase's transaction pooler: what one
    /// client leaves on a session there, the next client finds.
    /// </summary>
    Transaction,
}
