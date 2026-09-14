using DDDToolkit.Mediator.Tests.Domain;
using Mediator;

namespace DDDToolkit.Mediator.Tests.Infrastructure;

/// <summary>Records every <see cref="BasketCreated"/>.</summary>
public sealed class BasketCreatedHandler(EventLog log) : INotificationHandler<BasketCreated>
{
    public ValueTask Handle(BasketCreated notification, CancellationToken cancellationToken)
    {
        log.Record(nameof(BasketCreatedHandler), notification);
        return default;
    }
}

/// <summary>A second handler for the same event, to show that fan-out runs each handler once.</summary>
public sealed class BasketCreatedAuditHandler(EventLog log) : INotificationHandler<BasketCreated>
{
    public ValueTask Handle(BasketCreated notification, CancellationToken cancellationToken)
    {
        log.Record(nameof(BasketCreatedAuditHandler), notification);
        return default;
    }
}

/// <summary>Records every <see cref="ItemAdded"/>.</summary>
public sealed class ItemAddedHandler(EventLog log) : INotificationHandler<ItemAdded>
{
    public ValueTask Handle(ItemAdded notification, CancellationToken cancellationToken)
    {
        log.Record(nameof(ItemAddedHandler), notification);
        return default;
    }
}

/// <summary>
/// Injects the <see cref="BasketContext"/>, which only works because Mediator is registered with
/// <c>ServiceLifetime.Scoped</c>. It hands the context it was given to the log so a test can compare
/// it with the one that is saving.
/// </summary>
public sealed class BasketEmptiedHandler(EventLog log, BasketContext context) : INotificationHandler<BasketEmptied>
{
    public ValueTask Handle(BasketEmptied notification, CancellationToken cancellationToken)
    {
        log.ContextSeenByHandler = context;
        log.Record(nameof(BasketEmptiedHandler), notification);
        return default;
    }
}
