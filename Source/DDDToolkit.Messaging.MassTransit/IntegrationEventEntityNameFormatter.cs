using DDDToolkit.BaseTypes;
using MassTransit;

namespace DDDToolkit.Messaging.MassTransit;

/// <summary>
/// Names the exchange of every toolkit event after its published name and version,
/// <c>ordering.order-placed.v1</c>, instead of after its CLR type, <c>Shop.Ordering.Contracts:OrderPlacedV1</c>.
/// Every other message type keeps the name <paramref name="fallback"/> gives it.
/// </summary>
/// <remarks>
/// MassTransit gives each message type an exchange of its own and binds each consumer's queue to it, both
/// through this formatter, so the sending and the receiving service agree as long as both use it. What it
/// buys is that the exchange no longer moves when the contract class is renamed or moved to another
/// namespace: the name stays what <c>[IntegrationEvent]</c> or the convention says it is. Install it with
/// <see cref="MassTransitExtensions.UseIntegrationEventNames"/>.
/// <para>
/// A service that switches over gets new exchanges. Messages already queued are not affected, but a service
/// that still publishes to the old exchange reaches nobody who switched, so switch all of them together.
/// </para>
/// </remarks>
/// <param name="fallback">The formatter for every message type the toolkit does not name, usually the one MassTransit had.</param>
public sealed class IntegrationEventEntityNameFormatter(IEntityNameFormatter fallback) : IEntityNameFormatter
{
    private readonly IEntityNameFormatter _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

    /// <inheritdoc />
    public string FormatEntityName<T>()
        => IntegrationEventContract.IsNamedByToolkit(typeof(T))
            ? IntegrationEventContract.EntityNameOf(typeof(T))
            : _fallback.FormatEntityName<T>();
}
