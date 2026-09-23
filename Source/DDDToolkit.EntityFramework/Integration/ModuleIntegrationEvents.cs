using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// The integration events one consuming module handles, each guarded by that module's own inbox.
/// Configure it through <c>services.AddModuleIntegrationEvents&lt;TContext&gt;(module =&gt; ...)</c>.
/// <code>
/// services.AddModuleIntegrationEvents&lt;ShippingContext&gt;(module => module
///     .Handle&lt;OrderPlacedV1, BookShipment&gt;()
///     .Handle&lt;OrderCancelledV1, CancelShipment&gt;());
/// </code>
/// <para>
/// The handlers belong to the module that registers them. <see cref="ModuleIntegrationEventSink"/> offers
/// every message to every consuming module, and each module runs only its own handlers, under its own
/// inbox, in its own context. A second consuming module therefore changes nothing for the first, and the
/// producing module never names either of them.
/// </para>
/// </summary>
/// <typeparam name="TContext">The consuming module's context: its inbox records which handler applied which message.</typeparam>
public sealed class ModuleIntegrationEvents<TContext> where TContext : DbContext
{
    private readonly IServiceCollection _services;
    private readonly ModuleConsumerRegistration<TContext> _registration;
    private readonly IntegrationEventSubscriptions _subscriptions;

    internal ModuleIntegrationEvents(IServiceCollection services, ModuleConsumerRegistration<TContext> registration, IntegrationEventSubscriptions subscriptions)
    {
        _services = services;
        _registration = registration;
        _subscriptions = subscriptions;
    }

    /// <summary>
    /// Hands every message published as <typeparamref name="TContract"/> to the
    /// <typeparamref name="THandler"/> that <paramref name="create"/> builds, scoped, from the scope the
    /// message is delivered in. It runs inside this module's inbox under <paramref name="consumer"/>.
    /// <para>
    /// Everything the other overload reads off attributes is passed in here: the contract's published
    /// name, which goes into <see cref="IntegrationEventSubscriptions"/>, and the consumer name the inbox
    /// keys on. You rarely write this call: the generated <c>module.Add{Module}IntegrationEvents()</c>
    /// writes one per handler in the module, with the names its compiler saw.
    /// </para>
    /// </summary>
    /// <param name="contract">The published name of <typeparamref name="TContract"/>.</param>
    /// <param name="consumer">The name the inbox records this handler under.</param>
    /// <param name="create">Builds the handler, taking its dependencies from the delivery's scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="contract"/> or <paramref name="consumer"/> is empty, or this module already has a handler under the same consumer name.</exception>
    public ModuleIntegrationEvents<TContext> Handle<TContract, THandler>(string contract, string consumer, Func<IServiceProvider, THandler> create)
        where TContract : class
        where THandler : class, IIntegrationEventHandler<TContract>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentNullException.ThrowIfNull(create);

        _services.TryAddScoped(create);
        _registration.Add(new ModuleHandler(
            consumer,
            static received => received is TContract,
            static (services, received, message, cancellationToken) =>
                services.GetRequiredService<THandler>().HandleAsync((TContract)received, message, cancellationToken)));
        _subscriptions.Handles<TContract>(contract);

        return this;
    }

    /// <summary>
    /// Hands every message published as <typeparamref name="TContract"/> to <typeparamref name="THandler"/>,
    /// resolved (scoped) from the scope the outbox processor runs in. It runs inside this module's inbox
    /// under its consumer name, so name it with <c>[IntegrationEventConsumer("...")]</c>: the name is what
    /// the inbox remembers. The consumer name and the contract's published name are read off their
    /// attributes at run time; the generated <c>module.Add{Module}IntegrationEvents()</c> does without.
    /// </summary>
    /// <exception cref="ArgumentException">This module already has a handler under the same consumer name.</exception>
    public ModuleIntegrationEvents<TContext> Handle<TContract, THandler>()
        where TContract : class
        where THandler : class, IIntegrationEventHandler<TContract>
    {
        _services.TryAddScoped<THandler>();
        _registration.Add(new ModuleHandler(
            IntegrationEventConsumer.NameOf<THandler>(),
            static contract => contract is TContract,
            static (services, contract, message, cancellationToken) =>
                services.GetRequiredService<THandler>().HandleAsync((TContract)contract, message, cancellationToken)));
        _subscriptions.Handles<TContract>(IntegrationEventContract.NameOf<TContract>());

        return this;
    }

    /// <summary>
    /// Hands every message published as <typeparamref name="TContract"/> to <paramref name="handler"/>, an
    /// instance you already own. Tests usually want this one.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="ArgumentException">This module already has a handler under the same consumer name.</exception>
    public ModuleIntegrationEvents<TContext> Handle<TContract>(IIntegrationEventHandler<TContract> handler) where TContract : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        _registration.Add(new ModuleHandler(
            IntegrationEventConsumer.NameOf(handler.GetType()),
            static contract => contract is TContract,
            (_, contract, message, cancellationToken) => handler.HandleAsync((TContract)contract, message, cancellationToken)));
        _subscriptions.Handles<TContract>(IntegrationEventContract.NameOf<TContract>());

        return this;
    }
}

/// <summary>
/// One handler of one module, captured when it was registered: its consumer name, which contracts it
/// accepts and how to call it. Captured as delegates over the generic arguments, so delivering a message
/// needs no reflection.
/// </summary>
internal sealed record ModuleHandler(
    string Consumer,
    Func<object, bool> Accepts,
    Func<IServiceProvider, object, IntegrationEventMessage, CancellationToken, Task> Invoke);

/// <summary>The handlers one consuming module registered, gathered over every call for that context.</summary>
internal sealed class ModuleConsumerRegistration<TContext> where TContext : DbContext
{
    private readonly List<ModuleHandler> _handlers = [];

    public IReadOnlyList<ModuleHandler> Handlers => _handlers;

    public void Add(ModuleHandler handler)
    {
        if (_handlers.Any(existing => string.Equals(existing.Consumer, handler.Consumer, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"{typeof(TContext).Name} already has a handler under the consumer name '{handler.Consumer}'. The inbox keys on that name, " +
                "so the second handler would be skipped as having already applied every message. Give it its own [IntegrationEventConsumer(\"...\")].",
                nameof(handler));
        }

        _handlers.Add(handler);
    }
}

/// <summary>What <see cref="ModuleIntegrationEventSink"/> offers each message to: one per consuming module.</summary>
internal interface IModuleIntegrationEventConsumer
{
    /// <summary>The consuming module's context, for logs and errors.</summary>
    string Module { get; }

    /// <summary>
    /// Runs this module's handlers that accept <paramref name="contract"/>, each inside the module's inbox.
    /// Returns the consumers that threw, with what they threw; an empty list means the module is done.
    /// </summary>
    Task<IReadOnlyList<(string Consumer, Exception Failure)>> DeliverAsync(object contract, IntegrationEventMessage message, CancellationToken cancellationToken);
}

internal sealed class ModuleIntegrationEventConsumer<TContext>(
    ModuleConsumerRegistration<TContext> registration,
    IServiceProvider services) : IModuleIntegrationEventConsumer where TContext : DbContext
{
    private readonly ILogger _logger = services.GetService<ILogger<ModuleIntegrationEventConsumer<TContext>>>()
        ?? (ILogger)NullLogger.Instance;

    public string Module => typeof(TContext).Name;

    public async Task<IReadOnlyList<(string Consumer, Exception Failure)>> DeliverAsync(object contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        List<(string, Exception)>? failures = null;
        DomainEventInbox<TContext>? inbox = null;

        foreach (var handler in registration.Handlers)
        {
            if (!handler.Accepts(contract))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            inbox ??= services.GetRequiredService<DomainEventInbox<TContext>>();

            try
            {
                // Each handler owns its inbox row, so a failure here does not undo the handlers that ran
                // before it and does not make them run again on the retry.
                var applied = await inbox.ExecuteOnceAsync(
                    message,
                    handler.Consumer,
                    (received, token) => handler.Invoke(services, contract, received, token),
                    cancellationToken).ConfigureAwait(false);

                if (!applied)
                {
                    _logger.LogDebug("Consumer {Consumer} in {Module} had already applied message {MessageId}.", handler.Consumer, Module, message.MessageId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Consumer {Consumer} in {Module} failed on message {MessageId} ({Name}).", handler.Consumer, Module, message.MessageId, message.Name);
                (failures ??= []).Add((handler.Consumer, exception));
            }
        }

        return failures ?? [];
    }
}
