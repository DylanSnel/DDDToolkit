using System.Collections.Concurrent;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.EntityFramework.Postgres.Pgmq;

/// <summary>
/// How the pgmq sink turns a published message into a queue row: which queue, and what rides alongside
/// the payload.
/// </summary>
public sealed class PgmqSinkOptions
{
    /// <summary>The queue used when nothing else is configured.</summary>
    public const string DefaultQueueName = "integration_events";

    private Func<IntegrationEventMessage, string> _queue = _ => DefaultQueueName;

    /// <summary>
    /// The queue a message goes to. One queue for everything by default, which is the shape that keeps
    /// ordering and monitoring simple; split it when one consumer's backlog must not hold up another's.
    /// </summary>
    public Func<IntegrationEventMessage, string> QueueName
    {
        get => _queue;
        set => _queue = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Sends every message to <paramref name="queue"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    public PgmqSinkOptions UseQueue(string queue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        _queue = _ => queue;
        return this;
    }

    /// <summary>
    /// Picks the queue per message, usually from <see cref="IntegrationEventMessage.Name"/>.
    /// <para>
    /// pgmq builds table names from the queue name, so keep them short, lower case and free of anything
    /// that is not a letter, a digit or an underscore. A published name like
    /// <c>ordering.order-placed</c> has to be rewritten, not passed through.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="queue"/> is null.</exception>
    public PgmqSinkOptions UseQueue(Func<IntegrationEventMessage, string> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
        return this;
    }

    /// <summary>
    /// Creates a queue the first time the sink sends to it. On by default, because a queue that does not
    /// exist yet is the normal state of a new deployment and <c>pgmq.create</c> is idempotent.
    /// <para>
    /// Turn it off when the application's database user is not allowed to create tables, and create the
    /// queues in a migration instead. The sink then fails on a missing queue rather than hiding it.
    /// </para>
    /// </summary>
    public bool CreateQueueIfMissing { get; set; } = true;

    /// <summary>
    /// Writes the envelope's routing fields into pgmq's <c>headers</c> column: the message id, the
    /// published name, the version, the occurrence time and the aggregate. On by default, because a
    /// consumer can then filter and trace without parsing the body, and because a human reading the
    /// queue table can see what a row is.
    /// </summary>
    public bool SendHeaders { get; set; } = true;

    /// <summary>
    /// Queues this process has already made sure of, so the idempotent create runs once per queue per
    /// database rather than on every message. It lives here because the options are the one object every
    /// sink instance shares.
    /// </summary>
    internal ConcurrentDictionary<string, bool> KnownQueues { get; } = new(StringComparer.Ordinal);
}
