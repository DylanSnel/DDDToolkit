namespace DDDToolkit.Startup;

/// <summary>
/// The start-up checks of one host: the ones its registrations brought, the ones it turned off and why, and whether
/// it runs them. <see cref="StartupCheckServiceCollectionExtensions"/> keeps one in the host's services, and the
/// runner reads it when the host starts; a test reads it to see what a host would run, and in which order.
/// </summary>
public sealed class StartupChecks
{
    /// <summary>
    /// The key of <see cref="Exception.Data"/> under which the exception a check refused the start with carries the
    /// check's name: the exception is thrown as the check threw it, so its type and message are the check's own.
    /// </summary>
    public const string FailedCheckKey = "DDDToolkit.StartupCheck";

    private readonly List<StartupCheck> _registered = [];
    private readonly Dictionary<string, string> _skipped = new(StringComparer.Ordinal);

    /// <summary>Every check the registrations brought, once per name, in the order they were registered.</summary>
    public IReadOnlyList<StartupCheck> Registered => _registered;

    /// <summary>The checks the host turned off, by name, each with the reason it gave.</summary>
    public IReadOnlyDictionary<string, string> Skipped => _skipped;

    /// <summary>
    /// The reason the host gave for turning every check off
    /// (<see cref="StartupCheckServiceCollectionExtensions.SkipStartupChecks"/>), or null where it did not.
    /// </summary>
    public string? AllSkippedReason { get; private set; }

    /// <summary>
    /// Whether the host asked for its checks (<see cref="StartupCheckServiceCollectionExtensions.RunStartupChecks"/>).
    /// Without that, none of them runs.
    /// </summary>
    public bool RunsAll { get; private set; }

    /// <summary>
    /// Every registered check in the order the runner takes them, the ones that will not run included: by stage,
    /// then, within a stage, as registered, with each check moved in front of the ones it says it runs before.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A check says it runs before one of an earlier stage, or the checks of a stage say they each run before the
    /// next in a circle. The message names them.
    /// </exception>
    public IReadOnlyList<StartupCheck> InOrder() => Order(_registered);

    /// <summary>
    /// <paramref name="those"/>, checks of these, in the order the runner takes them; what they say of a check that
    /// is not among them is passed over.
    /// </summary>
    internal IReadOnlyList<StartupCheck> Order(IReadOnlyList<StartupCheck> those)
    {
        var stageOf = those.ToDictionary(check => check.Name, check => check.Stage, StringComparer.Ordinal);
        foreach (var check in those)
        {
            foreach (var later in check.RunsBefore)
            {
                if (stageOf.TryGetValue(later, out var stage) && stage < check.Stage)
                {
                    throw new InvalidOperationException(
                        $"The start-up check '{check.Name}' runs in the stage {check.Stage} and says it runs before '{later}', which runs in the earlier stage {stage}. " +
                        "A check runs before every check of a later stage already, and after every check of an earlier one: move one of them to the other's stage, or take the name out.");
                }
            }
        }

        var ordered = new List<StartupCheck>(those.Count);
        foreach (var stage in those.Select(check => check.Stage).Distinct().Order())
        {
            ordered.AddRange(Ordered([.. those.Where(check => check.Stage == stage)]));
        }

        return ordered;
    }

    /// <summary>Adds <paramref name="check"/>, unless a check of its name is here already.</summary>
    internal void Add(StartupCheck check)
    {
        if (!_registered.Any(registered => registered.Name == check.Name))
        {
            _registered.Add(check);
        }
    }

    /// <summary>Turns the check named <paramref name="name"/> off; a second reason replaces the first.</summary>
    internal void Skip(string name, string reason) => _skipped[name] = reason;

    /// <summary>Turns every check off; a second reason replaces the first.</summary>
    internal void SkipAll(string reason) => AllSkippedReason = reason;

    /// <summary>Has the host run the checks.</summary>
    internal void RunAll() => RunsAll = true;

    /// <summary>
    /// The checks of one stage in their order: the first as registered of those no other check of the stage still
    /// has to run before, again and again. What is left once none is free waits on a circle.
    /// </summary>
    private static List<StartupCheck> Ordered(List<StartupCheck> stage)
    {
        // For each check, the checks of the stage that say they run before it. A check that names itself is no
        // reason to wait.
        var waitsFor = stage.ToDictionary(
            check => check.Name,
            check => stage.Where(other => other != check && other.RunsBefore.Contains(check.Name, StringComparer.Ordinal)).Select(other => other.Name).ToList(),
            StringComparer.Ordinal);

        var ordered = new List<StartupCheck>(stage.Count);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var left = new List<StartupCheck>(stage);
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(check => waitsFor[check.Name].All(done.Contains))
                ?? throw new InvalidOperationException(
                    $"The start-up checks {string.Join(", ", left.Select(check => "'" + check.Name + "'"))} of the stage {left[0].Stage} cannot be put in an order: " +
                    "some of them say they run before one another in a circle, so none of them can run first. Take one of the names out of RunsBefore.");

            done.Add(next.Name);

            ordered.Add(next);
            left.Remove(next);
        }

        return ordered;
    }
}
