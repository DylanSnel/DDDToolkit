namespace DDDToolkit.Access;

/// <summary>
/// How strictly the host wants every flow of work to say who it runs as. Registered as a singleton by
/// <see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>; without it nothing is required,
/// as in every 3.x host.
/// </summary>
public sealed class CallerOptions
{
    /// <summary>
    /// Whether work that nobody said anything about fails instead of running as the system.
    /// <para>
    /// Off, the default, an accessor that knows no caller answers <see cref="Abstractions.Access.Caller.System"/>,
    /// which on a Postgres with row level security is the role the application logged in as: usually the
    /// tables' owner, past every policy. On, it throws <see cref="NoCallerException"/> instead. The toolkit
    /// then begins the system caller around its own bookkeeping only (reading and marking outbox rows,
    /// receiving from a transport, checking migrations), runs the handlers it calls with no caller at all
    /// (<see cref="Callers.BeginNone"/>), and refuses a caller that changes while a transaction is open on a
    /// connection, where it would otherwise only log it.
    /// </para>
    /// </summary>
    public bool RequireExplicitCallers { get; set; }
}
