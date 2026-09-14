using DDDToolkit.BaseTypes;

namespace DDDToolkit.Interfaces;

/// <summary>
/// Where a message goes when it leaves this process. Implement it once per transport: a bus, a queue,
/// a webhook, a log file for a test.
/// <para>
/// This is the outbox's exit. The outbox makes an event durable, but something has to carry it
/// outside, and the toolkit deliberately ships no adapter for any particular broker. Register one or
/// more with <c>options.UseOutbox(outbox =&gt; outbox.SendTo&lt;MySink&gt;())</c>.
/// </para>
/// <code>
/// public sealed class WebhookSink(HttpClient client) : IIntegrationEventSink
/// {
///     public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken)
///     {
///         using var content = new StringContent(message.Payload, Encoding.UTF8, message.ContentType);
///         content.Headers.Add("X-Message-Id", message.MessageId.ToString());
///         content.Headers.Add("X-Message-Name", $"{message.Name}/v{message.Version}");
///
///         var response = await client.PostAsync("/events", content, cancellationToken);
///         response.EnsureSuccessStatusCode();
///     }
/// }
/// </code>
/// <para>
/// Throw to say delivery failed. The outbox processor records the failure on the row and retries the
/// whole message later, so a sink that already accepted it will see it again: every sink must assume
/// at-least-once and every consumer must key idempotency on
/// <see cref="IntegrationEventMessage.MessageId"/>.
/// </para>
/// </summary>
public interface IIntegrationEventSink
{
    /// <summary>
    /// Delivers <paramref name="message"/>. Returning means the transport accepted it; throwing means
    /// it did not and the message should be retried.
    /// </summary>
    /// <param name="message">The message to deliver.</param>
    /// <param name="cancellationToken">Cancels the delivery attempt.</param>
    Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default);
}
