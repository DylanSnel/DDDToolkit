namespace DDDToolkit.Access;

/// <summary>
/// Whether the host requires every flow of work to say who it runs as. Registered as a singleton by
/// <see cref="CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser"/>. Without it nothing is required.
/// </summary>
public sealed class CurrentUserOptions
{
    /// <summary>
    /// Whether work that began no user fails instead of running as system work.
    /// <para>
    /// Off, the default, <see cref="CurrentUser"/> answers system work where nothing was begun and the host knows no
    /// user, <see cref="Abstractions.Access.CurrentUserSnapshot.System"/>. It is the system work
    /// <see cref="AccessManager.BeginSystemBypassingRowAccess"/> begins, which no row access rule holds. On a Postgres
    /// with row level security that is the role the application logged in as, usually the tables' owner, past every
    /// policy. On, it throws <see cref="NoCurrentUserException"/> instead. The toolkit then begins that system work
    /// around its own bookkeeping only: reading and marking outbox rows, receiving from a transport, checking migrations. It runs the handlers it
    /// calls with nobody begun (<see cref="AccessManager.BeginNone"/>). It refuses a user that
    /// changes while a transaction is open on a connection, where it would otherwise only log it.
    /// </para>
    /// </summary>
    public bool RequireExplicitCurrentUser { get; set; }
}
