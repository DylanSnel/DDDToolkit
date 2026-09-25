using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace DDDToolkit.Messaging.Postgres;

/// <summary>
/// The pgmq functions this package calls, wrapped so a caller passes a connection and gets .NET types
/// back. Every method takes the connection and the transaction explicitly, which is the point: pass the
/// ones your unit of work is already using and the enqueue is part of it.
/// <para>
/// pgmq is a Postgres extension whose queues are ordinary tables. <c>pgmq.send</c> is an insert, so it
/// obeys the transaction it is called in. That is what makes it different from every broker: there is no
/// window in which the aggregate is committed and the message is not, because they are the same commit.
/// Supabase Queues is this extension with a UI on top, and nothing here knows or cares about Supabase.
/// </para>
/// <para>
/// The functions are verified against pgmq 1.5.1, the version Supabase ships, and 1.13.0; the topic
/// functions need 1.11 or later (<see cref="TopicRoutingVersion"/>). Names and argument names are passed
/// explicitly, because <c>send</c> is overloaded on its third argument (<c>headers jsonb</c> against
/// <c>delay integer</c>) and positional arguments would pick whichever Postgres liked.
/// </para>
/// </summary>
public static class PgmqQueue
{
    /// <summary>The schema the extension creates and the toolkit never writes to directly.</summary>
    public const string Schema = "pgmq";

    /// <summary>
    /// The first pgmq with topic routing (<c>pgmq.send_topic</c>, <c>pgmq.bind_topic</c>), which
    /// <see cref="PgmqSinkOptions.UseTopics"/> and <see cref="PgmqConsumerOptions.BindTopics"/> need.
    /// </summary>
    public static readonly Version TopicRoutingVersion = new(1, 11);

    /// <summary>Postgres says the schema is missing.</summary>
    private const string InvalidSchemaName = "3F000";

    /// <summary>Postgres says the function is missing.</summary>
    private const string UndefinedFunction = "42883";

    /// <summary>Whether the pgmq extension is installed in the connection's database.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    public static async Task<bool> IsInstalledAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = Command(connection, transaction, "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pgmq')");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Throws <see cref="PgmqNotInstalledException"/> when the extension is missing, so the failure names
    /// the problem rather than arriving as a raw SQL error from the first send.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    public static async Task EnsureInstalledAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        if (!await IsInstalledAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            throw new PgmqNotInstalledException(connection.Database);
        }
    }

    /// <summary>
    /// The version of the pgmq extension installed in the connection's database, as
    /// <c>pg_extension.extversion</c> gives it, or <see langword="null"/> when it is not installed.
    /// <para>
    /// This is the version the database runs, not the newest the server could install; after the server
    /// gets a newer pgmq the database keeps the old one until <c>ALTER EXTENSION pgmq UPDATE</c>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="FormatException">The extension reports a version that is not a number.</exception>
    public static async Task<Version?> InstalledVersionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = Command(connection, transaction, "SELECT extversion FROM pg_extension WHERE extname = 'pgmq'");
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string version
            ? ParseVersion(version)
            : null;
    }

    /// <summary>
    /// Throws when the database cannot route by topic: <see cref="PgmqNotInstalledException"/> without the
    /// extension, <see cref="PgmqTopicsNotSupportedException"/> when it is older than
    /// <see cref="TopicRoutingVersion"/>. The sink and the consumer run the same check at start-up when
    /// they are registered with topics.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    /// <exception cref="PgmqTopicsNotSupportedException">The extension predates topic routing.</exception>
    public static async Task EnsureTopicRoutingAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var version = await InstalledVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
            ?? throw new PgmqNotInstalledException(connection.Database);

        if (version < TopicRoutingVersion)
        {
            throw new PgmqTopicsNotSupportedException(connection.Database, version);
        }
    }

    /// <summary>
    /// <c>1.5.1</c> as a <see cref="Version"/>. Anything after the numbers, a pre-release suffix for
    /// instance, is ignored, and a bare major version gets a minor of zero.
    /// </summary>
    private static Version ParseVersion(string text)
    {
        var numbers = new string(text.TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray()).TrimEnd('.');

        if (!numbers.Contains('.', StringComparison.Ordinal))
        {
            numbers += ".0";
        }

        return Version.TryParse(numbers, out var version)
            ? version
            : throw new FormatException($"The pgmq extension reports version '{text}', which is not a version number.");
    }

    /// <summary>
    /// Creates <paramref name="queue"/> if it does not exist. pgmq's own <c>create</c> is idempotent: a
    /// queue that is already there produces notices and no error.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    public static async Task CreateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string queue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        await using var command = Command(connection, transaction, "SELECT pgmq.create(queue_name => $1)");
        command.Parameters.Add(Text(queue));

        await ExecuteAsync(connection, command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Enqueues <paramref name="body"/> on <paramref name="queue"/> and returns pgmq's message id. When
    /// <paramref name="transaction"/> is the transaction writing your aggregate, the message appears if
    /// and only if that transaction commits.
    /// </summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="transaction">The transaction to enqueue in, or <see langword="null"/> to commit on its own.</param>
    /// <param name="queue">The queue name.</param>
    /// <param name="body">The message body, which must be valid JSON: pgmq stores it as <c>jsonb</c>.</param>
    /// <param name="headers">Routing metadata as JSON, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="body"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    public static async Task<long> SendAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string queue, string body, string? headers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        await using var command = Command(connection, transaction, "SELECT * FROM pgmq.send(queue_name => $1, msg => $2, headers => $3)");
        command.Parameters.Add(Text(queue));
        command.Parameters.Add(Json(body));
        command.Parameters.Add(Json(headers));

        var id = await ExecuteScalarAsync(connection, command, cancellationToken).ConfigureAwait(false);
        return (long)id!;
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> messages and hides them from other readers for
    /// <paramref name="visibilityTimeout"/> seconds. Archive or delete each one before that runs out, or
    /// it is handed to somebody else as well.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1 or <paramref name="visibilityTimeout"/> is negative.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    public static async Task<IReadOnlyList<PgmqMessage>> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string queue,
        int visibilityTimeout = 30,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(visibilityTimeout);

        await using var command = Command(
            connection,
            transaction,
            "SELECT msg_id, read_ct, enqueued_at, vt, message, headers FROM pgmq.read(queue_name => $1, vt => $2, qty => $3)");
        command.Parameters.Add(Text(queue));
        command.Parameters.Add(Integer(visibilityTimeout));
        command.Parameters.Add(Integer(count));

        return await ReadMessagesAsync(connection, command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads like <see cref="ReadAsync"/>, but when the queue is empty waits inside Postgres, with
    /// <c>pgmq.read_with_poll</c>, for up to <paramref name="maxPollSeconds"/> for a message to arrive. It
    /// returns as soon as there is one, and empty when the time runs out.
    /// <para>
    /// Postgres looks at the queue again every <paramref name="pollIntervalMilliseconds"/>, which costs no
    /// round trip, so a message is picked up within that interval rather than after the client's next
    /// poll. The price is the connection: it is busy for as long as the read waits. The command timeout is
    /// lengthened by the wait, so the connection's own timeout still applies to the read itself.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/>, <paramref name="maxPollSeconds"/> or <paramref name="pollIntervalMilliseconds"/>
    /// is below 1, or <paramref name="visibilityTimeout"/> is negative.
    /// </exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    public static async Task<IReadOnlyList<PgmqMessage>> ReadWithPollAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string queue,
        int visibilityTimeout = 30,
        int count = 10,
        int maxPollSeconds = 5,
        int pollIntervalMilliseconds = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(visibilityTimeout);
        // pgmq checks the clock before its first look, so a zero wait would never read at all.
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPollSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pollIntervalMilliseconds, 1);

        await using var command = Command(
            connection,
            transaction,
            "SELECT msg_id, read_ct, enqueued_at, vt, message, headers FROM pgmq.read_with_poll(queue_name => $1, vt => $2, qty => $3, max_poll_seconds => $4, poll_interval_ms => $5)");
        command.Parameters.Add(Text(queue));
        command.Parameters.Add(Integer(visibilityTimeout));
        command.Parameters.Add(Integer(count));
        command.Parameters.Add(Integer(maxPollSeconds));
        command.Parameters.Add(Integer(pollIntervalMilliseconds));

        // Zero means no timeout at all, and stays that way.
        if (command.CommandTimeout > 0)
        {
            command.CommandTimeout += maxPollSeconds;
        }

        return await ReadMessagesAsync(connection, command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<PgmqMessage>> ReadMessagesAsync(NpgsqlConnection connection, NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var messages = new List<PgmqMessage>();

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(new PgmqMessage(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }
        catch (PostgresException exception) when (IsMissingExtension(exception))
        {
            throw new PgmqNotInstalledException(connection.Database, exception);
        }

        return messages;
    }

    /// <summary>
    /// Moves a message to the queue's archive table, where it stays readable. Returns
    /// <see langword="false"/> when the message was already gone.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    public static Task<bool> ArchiveAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string queue, long messageId, CancellationToken cancellationToken = default)
        => SingleMessageAsync(connection, transaction, "SELECT pgmq.archive(queue_name => $1, msg_id => $2)", queue, messageId, cancellationToken);

    /// <summary>
    /// Removes a message for good. Returns <see langword="false"/> when it was already gone. Prefer
    /// <see cref="ArchiveAsync"/> while you still want to be able to answer what was sent.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    public static Task<bool> DeleteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string queue, long messageId, CancellationToken cancellationToken = default)
        => SingleMessageAsync(connection, transaction, "SELECT pgmq.delete(queue_name => $1, msg_id => $2)", queue, messageId, cancellationToken);

    private static async Task<bool> SingleMessageAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, string queue, long messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        await using var command = Command(connection, transaction, sql);
        command.Parameters.Add(Text(queue));
        command.Parameters.Add(new NpgsqlParameter { Value = messageId, NpgsqlDbType = NpgsqlDbType.Bigint });

        var result = await ExecuteScalarAsync(connection, command, cancellationToken).ConfigureAwait(false);
        return result is true;
    }

    /// <summary>
    /// Sends <paramref name="body"/> to every queue bound to a pattern <paramref name="routingKey"/> matches,
    /// with <c>pgmq.send_topic</c>: pgmq's own publish and subscribe, the way a RabbitMQ topic exchange
    /// routes. Inside <paramref name="transaction"/> when there is one, so the message reaches all its
    /// queues or none. Returns how many queues it reached; none is not an error, it is a message nobody
    /// subscribed to.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="body"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="routingKey"/> is empty or white space.</exception>
    /// <exception cref="PgmqNotInstalledException">The extension is not installed.</exception>
    /// <exception cref="PgmqTopicsNotSupportedException">The installed pgmq predates topic routing, which came in 1.11.</exception>
    public static async Task<int> SendTopicAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string routingKey, string body, string? headers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        await using var command = Command(connection, transaction, "SELECT pgmq.send_topic(routing_key => $1, msg => $2, headers => $3, delay => 0)");
        command.Parameters.Add(Text(routingKey));
        command.Parameters.Add(Json(body));
        command.Parameters.Add(Json(headers));

        var reached = await TopicScalarAsync(connection, command, cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(reached, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Binds <paramref name="queue"/> to <paramref name="pattern"/> with <c>pgmq.bind_topic</c>, unless it is
    /// bound already: from then on every message sent with a matching routing key lands in the queue too.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> or <paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="PgmqTopicsNotSupportedException">The installed pgmq predates topic routing, which came in 1.11.</exception>
    public static async Task BindTopicAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string pattern, string queue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        if ((await TopicBindingsAsync(connection, transaction, queue, cancellationToken).ConfigureAwait(false)).Contains(pattern, StringComparer.Ordinal))
        {
            return;
        }

        await using var command = Command(connection, transaction, "SELECT pgmq.bind_topic(pattern => $1, queue_name => $2)");
        command.Parameters.Add(Text(pattern));
        command.Parameters.Add(Text(queue));
        await TopicScalarAsync(connection, command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the binding of <paramref name="queue"/> to <paramref name="pattern"/>. False when there was none.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> or <paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="PgmqTopicsNotSupportedException">The installed pgmq predates topic routing, which came in 1.11.</exception>
    public static async Task<bool> UnbindTopicAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string pattern, string queue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        await using var command = Command(connection, transaction, "SELECT pgmq.unbind_topic(pattern => $1, queue_name => $2)");
        command.Parameters.Add(Text(pattern));
        command.Parameters.Add(Text(queue));
        return await TopicScalarAsync(connection, command, cancellationToken).ConfigureAwait(false) is true;
    }

    /// <summary>The patterns <paramref name="queue"/> is bound to.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty or white space.</exception>
    /// <exception cref="PgmqTopicsNotSupportedException">The installed pgmq predates topic routing, which came in 1.11.</exception>
    public static async Task<IReadOnlyList<string>> TopicBindingsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string queue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        await using var command = Command(connection, transaction, "SELECT pattern FROM pgmq.list_topic_bindings(queue_name => $1)");
        command.Parameters.Add(Text(queue));

        var patterns = new List<string>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                patterns.Add(reader.GetString(0));
            }
        }
        catch (PostgresException exception) when (IsMissingTopicRouting(exception))
        {
            throw NoTopicRouting(connection, exception);
        }
        catch (PostgresException exception) when (IsMissingExtension(exception))
        {
            throw new PgmqNotInstalledException(connection.Database, exception);
        }

        return patterns;
    }

    private static async Task<object?> TopicScalarAsync(NpgsqlConnection connection, NpgsqlCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (IsMissingTopicRouting(exception))
        {
            throw NoTopicRouting(connection, exception);
        }
        catch (PostgresException exception) when (IsMissingExtension(exception))
        {
            throw new PgmqNotInstalledException(connection.Database, exception);
        }
    }

    /// <summary>The extension is there, its topic functions are not: a pgmq from before 1.11.</summary>
    private static bool IsMissingTopicRouting(PostgresException exception)
        => exception.SqlState == UndefinedFunction && exception.Message.Contains("topic", StringComparison.OrdinalIgnoreCase);

    /// <summary>The installed version is not known here: a query that failed has aborted any transaction it was in.</summary>
    private static PgmqTopicsNotSupportedException NoTopicRouting(NpgsqlConnection connection, PostgresException exception)
        => new(connection.Database, installedVersion: null, exception);

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
        => new(sql, connection) { Transaction = transaction };

    private static NpgsqlParameter Text(string value) => new() { Value = value, NpgsqlDbType = NpgsqlDbType.Text };

    private static NpgsqlParameter Integer(int value) => new() { Value = value, NpgsqlDbType = NpgsqlDbType.Integer };

    private static NpgsqlParameter Json(string? value)
        => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb };

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (IsMissingExtension(exception))
        {
            throw new PgmqNotInstalledException(connection.Database, exception);
        }
    }

    private static async Task<object?> ExecuteScalarAsync(NpgsqlConnection connection, NpgsqlCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (IsMissingExtension(exception))
        {
            throw new PgmqNotInstalledException(connection.Database, exception);
        }
    }

    /// <summary>
    /// A missing extension shows up as a missing schema or a missing function, depending on whether the
    /// <c>pgmq</c> schema was ever created. Neither says what to do, so both are translated.
    /// </summary>
    private static bool IsMissingExtension(PostgresException exception)
        => exception.SqlState is InvalidSchemaName or UndefinedFunction
            && exception.Message.Contains(Schema, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens <paramref name="connection"/> when it is closed, and says whether it has to be closed again
    /// afterwards. A connection somebody else owns, an Entity Framework one in particular, is left as it
    /// was found.
    /// </summary>
    internal static async Task<bool> EnsureOpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
        {
            return false;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

