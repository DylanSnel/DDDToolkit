using System.Collections.Concurrent;
using DDDToolkit.BaseTypes;
using Microsoft.Extensions.Configuration;

namespace DDDToolkit.Messaging.Postgres;

/// <summary>
/// How the pgmq sink turns a published message into a queue row: which queue, and what rides alongside
/// the payload.
/// <para>
/// A message is routed one way: by topic (<see cref="UseTopics"/>), to several queues
/// (<see cref="UseQueues"/>) or to one (<see cref="UseQueue(string)"/>). Whichever of them was called last
/// decides, so code can override what a configuration section (<see cref="ReadFrom"/>) said.
/// </para>
/// </summary>
public sealed class PgmqSinkOptions
{
    /// <summary>The queue used when nothing else is configured.</summary>
    public const string DefaultQueueName = "integration_events";

    private Func<IntegrationEventMessage, string> _queue = _ => DefaultQueueName;
    private Func<IntegrationEventMessage, IEnumerable<string>>? _queues;

    /// <summary>
    /// Sends each message with <c>pgmq.send_topic</c>, the contract's published name as the routing key, to
    /// every queue bound to it. This is pgmq's own publish and subscribe, and the one to use between
    /// services: the sender names no queue, and each consuming service binds its own queue to the contracts
    /// it handles (<see cref="PgmqConsumerOptions.BindTopics"/>), as a RabbitMQ consumer binds its queue to a
    /// topic exchange.
    /// <para>
    /// A message nobody is bound to reaches no queue, again as with a broker. The sends share the connection
    /// and the transaction, so a message reaches all its queues or none. Needs pgmq 1.11 or later, which
    /// <see cref="CheckExtensionOnStart"/> verifies at start-up; Supabase has 1.5.1, where
    /// <see cref="UseQueue(string)"/> and <see cref="UseQueues"/> work instead.
    /// </para>
    /// </summary>
    public PgmqSinkOptions UseTopics()
    {
        Topics = true;
        _queues = null;
        return this;
    }

    /// <summary>True when messages are routed by topic (<see cref="UseTopics"/>) rather than to named queues.</summary>
    public bool Topics { get; private set; }

    /// <summary>
    /// Sends each message to every queue <paramref name="queues"/> names: fan-out, for when several
    /// deployables each read a queue of their own and more than one of them consumes the same message.
    /// <para>
    /// pgmq is a queue, not a topic: a message read by one consumer is gone for the others. Giving every
    /// consuming service its own queue, and enqueueing a message once per service that wants it, is how
    /// a queue carries publish and subscribe. The enqueues share the connection and the transaction, so a
    /// message reaches all of its queues or none of them. A message that should go nowhere returns no
    /// queue at all.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="queues"/> is null.</exception>
    public PgmqSinkOptions UseQueues(Func<IntegrationEventMessage, IEnumerable<string>> queues)
    {
        ArgumentNullException.ThrowIfNull(queues);
        _queues = queues;
        Topics = false;
        return this;
    }

    /// <summary>The queues a message is enqueued on: those of <see cref="UseQueues"/>, or else the one <see cref="QueueName"/> names.</summary>
    internal IEnumerable<string> Queues(IntegrationEventMessage message)
        => _queues is { } queues ? queues(message) : [QueueName(message)];

    /// <summary>
    /// The queue a message goes to. One queue for everything by default, which is the shape that keeps
    /// ordering and monitoring simple; split it when one consumer's backlog must not hold up another's.
    /// </summary>
    public Func<IntegrationEventMessage, string> QueueName
    {
        get => _queue;
        set => RouteTo(value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Sends every message to <paramref name="queue"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    public PgmqSinkOptions UseQueue(string queue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        RouteTo(_ => queue);
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
        RouteTo(queue);
        return this;
    }

    /// <summary>One queue per message, instead of topics or several queues.</summary>
    private void RouteTo(Func<IntegrationEventMessage, string> queue)
    {
        _queue = queue;
        _queues = null;
        Topics = false;
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
    /// Checks at start-up, once per database, that the pgmq extension is installed, and with
    /// <see cref="UseTopics"/> that it is 1.11 or later, so a database that cannot take the messages fails
    /// the start by name instead of the first send. On by default; applies to a sink registered with
    /// <c>AddPgmqSink</c>.
    /// <para>
    /// The check needs the database at start-up. Turn it off when the application has to start without it,
    /// or when something in the application itself installs the extension later in its start.
    /// </para>
    /// </summary>
    public bool CheckExtensionOnStart { get; set; } = true;

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

    private static readonly Dictionary<string, Action<PgmqSinkOptions, IConfigurationSection>> Settings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Queue"] = (options, setting) => options.UseQueue(PgmqSettings.Name(setting)),
        ["Queues"] = (options, setting) =>
        {
            var queues = PgmqSettings.Names(setting);
            options.UseQueues(_ => queues);
        },
        [nameof(Topics)] = (options, setting) =>
        {
            if (PgmqSettings.Boolean(setting))
            {
                options.UseTopics();
            }
        },
        [nameof(CreateQueueIfMissing)] = (options, setting) => options.CreateQueueIfMissing = PgmqSettings.Boolean(setting),
        [nameof(SendHeaders)] = (options, setting) => options.SendHeaders = PgmqSettings.Boolean(setting),
        [nameof(CheckExtensionOnStart)] = (options, setting) => options.CheckExtensionOnStart = PgmqSettings.Boolean(setting),
    };

    /// <summary>
    /// Sets the options named in <paramref name="configuration"/>, a section such as <c>Pgmq:Sink</c>, and
    /// leaves the others as they are. Keys match without regard to case.
    /// <list type="bullet">
    /// <item><c>Queue</c>: one queue name, as <see cref="UseQueue(string)"/>.</item>
    /// <item><c>Queues</c>: a list of queue names, every message to each of them, as <see cref="UseQueues"/>.</item>
    /// <item><c>Topics</c>: <c>true</c> to route by topic, as <see cref="UseTopics"/>.</item>
    /// <item><c>CreateQueueIfMissing</c>, <c>SendHeaders</c>, <c>CheckExtensionOnStart</c>: <c>true</c> or <c>false</c>.</item>
    /// </list>
    /// <code>
    /// "Pgmq": { "Sink": { "Queue": "shop", "CheckExtensionOnStart": false } }
    /// </code>
    /// A queue per message, chosen from its name, is code: <see cref="UseQueue(Func{IntegrationEventMessage, string})"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The section has a key these options do not know, a value that does not parse, or more than one of
    /// <c>Queue</c>, <c>Queues</c> and <c>Topics</c>; the message says which.
    /// </exception>
    public PgmqSinkOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Keys in a section have no order that means anything, so two ways to route is a mistake, not an override.
        var routes = new List<string>();
        if (configuration.GetSection("Queue").Exists())
        {
            routes.Add("Queue");
        }

        if (configuration.GetSection("Queues").Exists())
        {
            routes.Add("Queues");
        }

        if (bool.TryParse(configuration[nameof(Topics)], out var topics) && topics)
        {
            routes.Add(nameof(Topics));
        }

        if (routes.Count > 1)
        {
            var where = configuration is IConfigurationSection section ? $"'{section.Path}'" : "The configuration";
            throw new InvalidOperationException($"{where} routes messages more than one way ({string.Join(", ", routes)}). Keep one of them.");
        }

        PgmqSettings.Read(configuration, this, Settings);
        return this;
    }
}
