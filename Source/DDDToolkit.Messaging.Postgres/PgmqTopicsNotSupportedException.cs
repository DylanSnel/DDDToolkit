namespace DDDToolkit.Messaging.Postgres;

/// <summary>
/// The database has pgmq, but a version from before topic routing, so <c>pgmq.send_topic</c> and
/// <c>pgmq.bind_topic</c> do not exist. <see cref="PgmqSinkOptions.UseTopics"/> and
/// <see cref="PgmqConsumerOptions.BindTopics"/> need pgmq <see cref="PgmqQueue.TopicRoutingVersion"/> or later.
/// <para>
/// The sink and the consumer check for this at start-up, so the failure comes before the first message
/// rather than with it. Supabase is the usual reason to see it: its projects have pgmq 1.5.1.
/// </para>
/// </summary>
public sealed class PgmqTopicsNotSupportedException : InvalidOperationException
{
    /// <summary>Says which database has which pgmq, what topics need, and what works instead.</summary>
    /// <param name="database">The database that was checked.</param>
    /// <param name="installedVersion">The pgmq it has, or <see langword="null"/> when that is not known.</param>
    /// <param name="inner">The Postgres error that gave it away, when there was one.</param>
    public PgmqTopicsNotSupportedException(string? database, Version? installedVersion, Exception? inner = null)
        : base(Describe(database, installedVersion), inner)
    {
        Database = database;
        InstalledVersion = installedVersion;
    }

    /// <summary>The database whose pgmq is too old.</summary>
    public string? Database { get; }

    /// <summary>
    /// The pgmq version installed there, or <see langword="null"/> when the failure came from a call to a
    /// topic function rather than from the check, and the version was not read.
    /// </summary>
    public Version? InstalledVersion { get; }

    /// <summary>The first pgmq with topic routing.</summary>
    public Version RequiredVersion => PgmqQueue.TopicRoutingVersion;

    private static string Describe(string? database, Version? installedVersion)
    {
        var where = string.IsNullOrEmpty(database) ? "this database" : $"database '{database}'";
        var has = installedVersion is null
            ? $"The pgmq extension in {where} has no topic routing (pgmq.send_topic, pgmq.bind_topic)"
            : $"The pgmq extension in {where} is version {installedVersion}";

        return $"{has}, and topics (UseTopics on the sink, BindTopics on the consumer) need pgmq {PgmqQueue.TopicRoutingVersion} or later. " +
            "Supabase ships pgmq 1.5.1 with Postgres 17 at the time of this release, so a Supabase project cannot route by topic yet. " +
            "Named queues work on every version: send with UseQueue or UseQueues instead of UseTopics, and leave BindTopics off. " +
            "Where the server has a newer pgmq available, ALTER EXTENSION pgmq UPDATE; brings the database up to it.";
    }
}
