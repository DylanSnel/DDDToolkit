namespace DDDToolkit.EntityFramework.Postgres.Pgmq;

/// <summary>
/// One row read off a pgmq queue, exactly as <c>pgmq.message_record</c> describes it.
/// </summary>
/// <param name="MessageId">
/// pgmq's own key, a bigint that counts up per queue. It is not the integration message id: that one
/// travels inside <see cref="Body"/> and is what a consumer keys idempotency on.
/// </param>
/// <param name="ReadCount">
/// How often this message has been read without being archived or deleted. A number climbing here is
/// a consumer that keeps failing, which is where you look for a poison message.
/// </param>
/// <param name="EnqueuedAt">When the producing transaction committed it.</param>
/// <param name="VisibleAt">
/// When it becomes visible to another reader again. Reading hides a message for the visibility timeout
/// you asked for; archive or delete it before then or somebody else gets it too.
/// </param>
/// <param name="Body">The message, as the JSON that was enqueued.</param>
/// <param name="Headers">The headers column, as JSON, or <see langword="null"/> when none were sent.</param>
public sealed record PgmqMessage(
    long MessageId,
    int ReadCount,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset VisibleAt,
    string Body,
    string? Headers);
