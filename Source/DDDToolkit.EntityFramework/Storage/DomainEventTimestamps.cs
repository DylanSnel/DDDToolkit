namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// What column the outbox and the inbox keep their timestamps in. The three outbox timestamps and the
/// inbox's <c>ProcessedAt</c> are <see cref="DateTimeOffset"/> in the model either way; this decides
/// what that becomes in the database.
/// <para>
/// The processor orders by <c>CreatedAt</c>, so whatever the column is, it has to sort as an instant.
/// That is the whole constraint, and it is why there is a choice at all: SQLite stores a
/// <see cref="DateTimeOffset"/> as text and refuses to order by it, so on SQLite, and only on SQLite,
/// the toolkit converts to a UTC <see cref="DateTime"/>.
/// </para>
/// </summary>
public enum DomainEventTimestamps
{
    /// <summary>
    /// Let the provider decide: its own instant type where it has one it can order, a UTC
    /// <see cref="DateTime"/> on SQLite where it does not. That means <c>datetimeoffset</c> on SQL
    /// Server and <c>timestamp with time zone</c> on PostgreSQL. This is the default.
    /// </summary>
    ProviderDefault = 0,

    /// <summary>
    /// A UTC <see cref="DateTime"/> column on every provider, SQLite's shape everywhere. This is what
    /// DDDToolkit wrote before the choice existed, so pass it to leave the columns of a database you
    /// already have alone. On PostgreSQL it makes no difference at all: both land in
    /// <c>timestamp with time zone</c>. On SQL Server it is the difference between <c>datetime2</c>
    /// and <c>datetimeoffset</c>.
    /// </summary>
    UtcDateTime = 1,
}
