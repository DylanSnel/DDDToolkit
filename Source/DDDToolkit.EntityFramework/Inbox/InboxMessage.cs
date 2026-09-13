namespace DDDToolkit.EntityFramework.Inbox;

/// <summary>
/// One row per message per consumer, written in the same transaction as whatever handling that
/// message did. Its presence means "this consumer has already applied this message", which is what
/// turns at-least-once delivery into exactly-once effects. Map it with
/// <c>modelBuilder.AddDomainEventInbox()</c>.
/// <para>
/// The key is the pair, not the message: two consumers of the same message each get their own row and
/// each run once. That is what lets you add a consumer later without replaying its messages through
/// the ones that already ran.
/// </para>
/// </summary>
public sealed class InboxMessage
{
    /// <summary>
    /// The transport's message id, which for messages out of a DDDToolkit outbox is the domain event's
    /// <c>EventId</c>. Half of the key.
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Who processed it. A stable name you choose for one logical consumer, such as
    /// <c>billing.order-projector</c>. The other half of the key.
    /// </summary>
    public string Consumer { get; set; } = string.Empty;

    /// <summary>The published name the message arrived under, kept for diagnostics. Optional.</summary>
    public string? MessageName { get; set; }

    /// <summary>When the handler finished and the row was written.</summary>
    public DateTimeOffset ProcessedAt { get; set; }
}
