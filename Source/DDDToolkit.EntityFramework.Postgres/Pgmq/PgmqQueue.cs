using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace DDDToolkit.EntityFramework.Postgres.Pgmq;

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
/// The functions are verified against pgmq 1.5.1. Names and argument names are passed explicitly, because
/// <c>send</c> is overloaded on its third argument (<c>headers jsonb</c> against <c>delay integer</c>)
/// and positional arguments would pick whichever Postgres liked.
/// </para>
/// </summary>
public static class PgmqQueue
{
    /// <summary>The schema the extension creates and the toolkit never writes to directly.</summary>
    public const string Schema = "pgmq";

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

