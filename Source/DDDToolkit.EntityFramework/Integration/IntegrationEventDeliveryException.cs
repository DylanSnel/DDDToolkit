namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// One or more sinks refused a message. Thrown inside the outbox processor, which records it on the
/// row and retries the message later, so you normally see it in <c>OutboxMessage.LastError</c> or in
/// the processor's log rather than in a catch block.
/// </summary>
public sealed class IntegrationEventDeliveryException : AggregateException
{
    /// <summary>Describes which sinks failed for which message.</summary>
    /// <param name="messageId">The message that could not be delivered.</param>
    /// <param name="failedSinks">The names of the sinks that threw, in delivery order.</param>
    /// <param name="failures">What each of them threw.</param>
    public IntegrationEventDeliveryException(Guid messageId, IReadOnlyList<string> failedSinks, IEnumerable<Exception> failures)
        : base($"Message {messageId} was refused by {string.Join(", ", failedSinks)}.", failures)
    {
        MessageId = messageId;
        FailedSinks = failedSinks;
    }

    /// <summary>The message that could not be delivered.</summary>
    public Guid MessageId { get; }

    /// <summary>The sinks that threw. Sinks not named here accepted the message and will see it again on the retry.</summary>
    public IReadOnlyList<string> FailedSinks { get; }
}
