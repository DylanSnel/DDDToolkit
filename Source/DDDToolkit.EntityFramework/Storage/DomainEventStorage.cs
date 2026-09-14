namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// Names and sizes shared by the outbox and the inbox, so the model builder extensions and the
/// hand-written migration helpers cannot drift apart.
/// </summary>
public static class DomainEventStorage
{
    /// <summary>
    /// The schema the outbox and inbox tables live in unless you say otherwise: <c>ddd</c>. Plumbing
    /// tables are not part of your domain schema, and keeping them apart makes them easy to grant,
    /// purge and ignore.
    /// <para>
    /// Pass <see langword="null"/> for the schema to put them in the provider's default schema
    /// instead. Providers with no notion of schemas, SQLite in particular, ignore it either way: the
    /// SQLite provider drops the schema when it writes the identifier, so the table is plain
    /// <c>OutboxMessages</c> and everything still works.
    /// </para>
    /// </summary>
    public const string DefaultSchema = "ddd";

    /// <summary>The default outbox table name.</summary>
    public const string DefaultOutboxTableName = "OutboxMessages";

    /// <summary>The default inbox table name.</summary>
    public const string DefaultInboxTableName = "InboxMessages";

    /// <summary>Longest error text kept on an outbox row; longer text is truncated.</summary>
    public const int MaxErrorLength = 4000;

    /// <summary>Longest event or message name.</summary>
    public const int MaxNameLength = 256;

    /// <summary>Longest consumer name on an inbox row.</summary>
    public const int MaxConsumerLength = 256;

    /// <summary>Longest aggregate CLR type name kept on an outbox row.</summary>
    public const int MaxAggregateTypeLength = 512;

    /// <summary>Longest aggregate key text kept on an outbox row.</summary>
    public const int MaxAggregateIdLength = 256;

    /// <summary>The provider name the SQLite provider reports; it has no schemas.</summary>
    internal const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
}
