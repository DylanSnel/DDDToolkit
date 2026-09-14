using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A transport that writes to a list. Stands in for a bus: it records what it was handed and can be
/// told to refuse, which is how the multi-sink failure behaviour is exercised.
/// </summary>
public class RecordingSink : IIntegrationEventSink
{
    private readonly List<IntegrationEventMessage> _messages = [];

    public IReadOnlyList<IntegrationEventMessage> Messages => _messages;

    /// <summary>Set to make the next sends throw, as a broken transport would.</summary>
    public bool Refuse { get; set; }

    /// <summary>How often <see cref="SendAsync"/> was entered, refusals included.</summary>
    public int Attempts { get; private set; }

    public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        Attempts++;

        if (Refuse)
        {
            throw new InvalidOperationException($"{GetType().Name} is down");
        }

        _messages.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>A second sink type, so two sinks can be told apart in the assertions.</summary>
public sealed class SecondRecordingSink : RecordingSink;

/// <summary>
/// Resolved from the container rather than handed over as an instance, to prove sinks can take
/// constructor dependencies.
/// </summary>
public sealed class InjectedSink(RecordingSink target) : IIntegrationEventSink
{
    public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
        => target.SendAsync(message, cancellationToken);
}
