using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// How a model says that a table only grows, for the two packages that have to agree on it:
/// <c>DDDToolkit.EntityFramework</c> marks an event log's table, and <c>DDDToolkit.EntityFramework.Postgres</c>
/// writes the guard and the privileges of a marked table. The second does not reference the first, so this one
/// file is compiled into both, as internal types: there is one spelling of the marks, not two to keep alike.
/// </summary>
internal static class KeptRows
{
    /// <summary>The annotation on an entity type whose table only grows; its value is <see langword="true"/>.</summary>
    public const string AppendOnlyAnnotation = "DDDToolkit:AppendOnly";

    /// <summary>
    /// The annotation that says for how many seconds such a table keeps a row before it may be deleted. Absent
    /// when a row is kept for as long as the table exists.
    /// </summary>
    public const string KeepForSecondsAnnotation = "DDDToolkit:AppendOnly:KeepForSeconds";

    /// <summary>The annotation that names the property a row's age is counted from.</summary>
    public const string RecordedAtAnnotation = "DDDToolkit:AppendOnly:RecordedAt";

    /// <summary>The outbox's entity type, by the name of its class: a script knows it without a reference to its package.</summary>
    public const string OutboxClrType = "DDDToolkit.EntityFramework.Outbox.OutboxMessage";

    /// <summary>The inbox's entity type, by the name of its class.</summary>
    public const string InboxClrType = "DDDToolkit.EntityFramework.Inbox.InboxMessage";

    /// <summary>
    /// The properties of an outbox row that say how its delivery went, by name: the only ones the outbox
    /// processor writes once the row is there. Everything else on the row is the event as it was raised, which
    /// nothing changes afterwards.
    /// </summary>
    public static readonly string[] OutboxDeliveryProperties = ["Attempts", "LastError", "NextAttemptAt", "ProcessedAt"];

    /// <summary>
    /// The longest a table can be told to keep a row: a thousand years. The guard takes the period off the
    /// current time, and a calendar does not go back much further. A row kept for good is said by giving no
    /// period at all.
    /// </summary>
    public static readonly TimeSpan LongestKeepFor = TimeSpan.FromDays(365_250);

    /// <summary>Whether <paramref name="entityType"/>'s table only grows.</summary>
    public static bool IsAppendOnly(IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(AppendOnlyAnnotation)?.Value is true;

    /// <summary>
    /// For how many seconds <paramref name="entityType"/>'s table keeps a row before it may be deleted, or
    /// <see langword="null"/> when it keeps every row for good.
    /// </summary>
    public static long? KeepForSecondsOf(IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(KeepForSecondsAnnotation)?.Value switch
        {
            long seconds => seconds,
            int seconds => seconds,
            _ => null,
        };

    /// <summary>The property a row's age is counted from, or <see langword="null"/> when the type names none it has.</summary>
    public static IReadOnlyProperty? RecordedAtOf(IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(RecordedAtAnnotation)?.Value is string name ? entityType.FindProperty(name) : null;

    /// <summary>
    /// <paramref name="keepFor"/> in the whole seconds the model and the guard count in, rounded up: a guard
    /// that kept a row for less than was asked would be off the wrong way. <see langword="null"/>, a row kept
    /// for good, stays <see langword="null"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keepFor"/> is zero, negative or longer than <see cref="LongestKeepFor"/>.</exception>
    public static long? SecondsOf(TimeSpan? keepFor, string paramName)
    {
        if (keepFor is not { } period)
        {
            return null;
        }

        if (period <= TimeSpan.Zero || period > LongestKeepFor)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                period,
                $"How long a row is kept is a positive period of at most {LongestKeepFor.TotalDays:0} days. To keep every row for as long as the table exists, leave it out.");
        }

        return (long)Math.Ceiling(period.TotalSeconds);
    }
}
