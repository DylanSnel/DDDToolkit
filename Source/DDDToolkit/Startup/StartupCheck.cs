namespace DDDToolkit.Startup;

/// <summary>
/// One check a host runs before it serves anything, registered by the package that knows what it checks, where it
/// registers what the check is about: that the database has every migration, that the role the host logs in as
/// holds nothing, that the policies are the ones the application's code writes. A check reads and changes
/// nothing. It throws, naming what is wrong and what puts it right, and so refuses the start.
/// <code>
/// services.AddStartupCheck(new StartupCheck(
///     "billing.ledger-in-place",
///     StartupCheckStage.Database,
///     (services, cancellationToken) => LedgerChecks.EnsureInPlaceAsync(services, cancellationToken)));
/// </code>
/// </summary>
/// <remarks>
/// A check is run by the host's one runner (<see cref="StartupCheckServiceCollectionExtensions.RunStartupChecks"/>),
/// in its stage, as the application itself (<c>Caller.System</c>), before the server binds its port. It is handed
/// the application's root services: what it needs of a scope, a context say, it takes in a scope of its own. It may
/// run more than once in a host that also calls the method behind it by hand, so it must leave nothing behind
/// that a second run would trip over.
/// </remarks>
public sealed class StartupCheck
{
    /// <summary>
    /// A check named <paramref name="name"/>, run in <paramref name="stage"/> by <paramref name="runAsync"/>.
    /// </summary>
    /// <param name="name">
    /// What the host turns it off by (<see cref="StartupCheckServiceCollectionExtensions.SkipStartupCheck"/>) and
    /// what the log says it as: the package's own prefix, a dot, and what holds, such as
    /// <c>supabase.migrations-applied</c>. A second check registered under a name already taken is not registered.
    /// </param>
    /// <param name="stage">When it runs; see <see cref="StartupCheckStage"/>.</param>
    /// <param name="runAsync">
    /// The check: handed the application's root services and the host's start-up cancellation, it returns when all
    /// is well and throws when it is not. What it throws stops the start as it is thrown, so its message is what the
    /// host's operator reads.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or holds white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is not one of the stages.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="runAsync"/> is null.</exception>
    public StartupCheck(string name, StartupCheckStage stage, Func<IServiceProvider, CancellationToken, Task> runAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(runAsync);

        if (name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"A start-up check's name holds no white space, and '{name}' does: it is written in code and in logs as one word.", nameof(name));
        }

        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "A start-up check runs in one of the stages StartupCheckStage names.");
        }

        Name = name;
        Stage = stage;
        RunAsync = runAsync;
    }

    /// <summary>What the host turns it off by, and what the log says it as.</summary>
    public string Name { get; }

    /// <summary>When it runs: every check of one stage before any of the next.</summary>
    public StartupCheckStage Stage { get; }

    /// <summary>The check itself, handed the application's root services and the host's start-up cancellation.</summary>
    public Func<IServiceProvider, CancellationToken, Task> RunAsync { get; }

    /// <summary>
    /// The checks of the same stage this one runs before, by name, whatever order they were registered in: for a
    /// check whose failure would hide theirs, or whose question theirs rely on. A name no registration brought is
    /// passed over, so a check may name one of a package the host does not use. Naming a check of an earlier stage
    /// is a contradiction, and the runner refuses the start with it, as it does a circle of checks that each run
    /// before the next.
    /// </summary>
    public IReadOnlyList<string> RunsBefore { get; init; } = [];

    /// <summary>
    /// Whether it runs in a host that never called
    /// <see cref="StartupCheckServiceCollectionExtensions.RunStartupChecks"/>: for a check its package ran by
    /// itself before the runner existed, which an application upgrading keeps as it had it. Every other check
    /// runs once the host asks for them. A check that is on by default is still turned off by name, or with the
    /// others, like any.
    /// </summary>
    public bool OnByDefault { get; init; }

    /// <inheritdoc />
    public override string ToString() => Name + " (" + Stage + ")";
}
