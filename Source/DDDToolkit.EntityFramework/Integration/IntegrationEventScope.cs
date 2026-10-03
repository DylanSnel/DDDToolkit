using DDDToolkit.BaseTypes;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// Begins what an inbound handler runs under, around everything its delivery does: the inbox's read, its
/// transaction, the handler, the save and the commit. Returns what ends it, or <see langword="null"/> when
/// nothing needs to be begun for this message. Added to a module with
/// <see cref="ModuleIntegrationEvents{TContext}.Around"/>.
/// <code>
/// services.AddModuleIntegrationEvents&lt;ShippingContext&gt;(module => module
///     .Around((services, message, contract) => Callers.Begin(Caller.SystemIn("shipping")))
///     .Handle&lt;OrderPlacedV1, BookShipment&gt;());
/// </code>
/// </summary>
/// <remarks>
/// A caller a handler begins itself starts after the inbox began its transaction and ends before the inbox
/// saves, so under row level security the handler's writes and its inbox row would run as whoever delivered
/// the message. A scope is begun before the inbox is asked anything, so the whole delivery runs as one caller.
/// </remarks>
/// <param name="services">The delivery's scope, which the handler and the module's context are resolved from.</param>
/// <param name="message">The message as it arrived.</param>
/// <param name="contract">The contract the handler is given, read from the message.</param>
public delegate IDisposable? IntegrationEventScope(IServiceProvider services, IntegrationEventMessage message, object contract);
