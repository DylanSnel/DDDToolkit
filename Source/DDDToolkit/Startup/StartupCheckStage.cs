namespace DDDToolkit.Startup;

/// <summary>
/// What a start-up check needs before it can say anything useful, which is the order the checks run in: every
/// check of one stage before any of the next. Within a stage they run in the order they were registered, unless
/// one says it runs before another (<see cref="StartupCheck.RunsBefore"/>).
/// </summary>
/// <remarks>
/// The stages are the order in which one failure hides another. A context wired wrong fails every question put to
/// the database through it; a login role that may not become the system caller fails every question asked as the
/// system caller; a database that misses a migration misses the policies and functions that migration made. Each
/// of those is said first, by the check that names it, so a host is told the cause rather than one of its effects.
/// </remarks>
public enum StartupCheckStage
{
    /// <summary>
    /// What the application's services say, and nothing of a database: which options are registered, and how each
    /// context is configured. What these find is a mistake in code, and a check that asks the database through a
    /// context wired wrong would fail for that reason without saying it, so they come first.
    /// </summary>
    Services = 0,

    /// <summary>
    /// What the role the host logs in as may do, asked as that role, before anything sets a caller on a
    /// connection: whether it may become every caller. The stages after it ask as the system caller, which
    /// switches to the system caller's role, and a login role that may not would fail there, on the switch,
    /// without saying why.
    /// </summary>
    Login = 1,

    /// <summary>
    /// Whether the database has every migration the host was built with. A policy or a function missing from a
    /// database that misses a migration is missing because of it, and this stage names the migration.
    /// </summary>
    Migrations = 2,

    /// <summary>
    /// Whether the database is set up as the application relies on: the role the host logs in as holds nothing,
    /// and the policies, functions, triggers, grants and extensions are the ones its code expects. Asked as the
    /// system caller, of a database that has every migration.
    /// </summary>
    Database = 3,
}
