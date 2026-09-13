using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Tests.Domain.Events;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A consuming module in miniature: it knows the contract and nothing about the shelf aggregate that
/// produced it. That is the point of the module sink, so the test handlers are typed accordingly.
/// </summary>
[IntegrationEventConsumer("library.shelf-board")]
public class ShelfBoard : IIntegrationEventHandler<ShelfOpenedV3>
{
    private readonly List<ShelfOpenedV3> _seen = [];

    public IReadOnlyList<ShelfOpenedV3> Seen => _seen;

    /// <summary>The envelopes the contracts arrived in, for the assertions about identity.</summary>
    public List<IntegrationEventMessage> Envelopes { get; } = [];

    /// <summary>Set to make the handler throw, as a consumer with a bug would.</summary>
    public bool Refuse { get; set; }

    /// <summary>How often the handler body ran, refusals included.</summary>
    public int Runs { get; private set; }

    public Task HandleAsync(ShelfOpenedV3 contract, IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        Runs++;

        if (Refuse)
        {
            throw new InvalidOperationException($"{GetType().Name} cannot handle this");
        }

        _seen.Add(contract);
        Envelopes.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>A second consumer of the same contract, so per-consumer progress can be told apart.</summary>
[IntegrationEventConsumer("search.shelf-index")]
public sealed class ShelfIndex : ShelfBoard;

/// <summary>A consumer with no attribute, so the fallback to the CLR type name is exercised.</summary>
public sealed class UnnamedShelfConsumer : ShelfBoard;
