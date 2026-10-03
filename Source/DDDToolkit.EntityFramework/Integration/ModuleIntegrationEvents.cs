using DDDToolkit.Access;
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
/// <para>
/// <b>Who the handlers run as.</b> <see cref="Around"/> begins what each delivery runs under, before the
/// inbox reads anything and until it committed. Where the host requires explicit callers
/// (<see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>) a module without one does not
/// run its handlers at all: the delivery fails with <see cref="NoCallerException"/>, and the outbox or the
/// transport tries it again.
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
    /// Begins <paramref name="scope"/> around each delivery to this module's handlers: before the inbox is
    /// asked whether the message was applied, and until its transaction committed or the handler failed. A
    /// caller begun there is the caller of the inbox's read, the handler's work, the save and the inbox row.
    /// <code>
    /// module.Around((services, message, contract) => Callers.Begin(Caller.SystemIn("shipping")));
    /// module.Around(IntegrationEventScopes.System);   // as the application itself, on purpose
    /// </code>
    /// May be called more than once, here or in another call for the same context: the scopes nest in the
    /// order they were added, the first outermost, and end in the opposite order. Each handler of the module
    /// gets a scope of its own.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    public ModuleIntegrationEvents<TContext> Around(IntegrationEventScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        _registration.AddScope(scope);
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

/// <summary>The handlers one consuming module registered, and the scopes around them, gathered over every call for that context.</summary>
internal sealed class ModuleConsumerRegistration<TContext> where TContext : DbContext
{
    private readonly List<ModuleHandler> _handlers = [];
    private readonly List<IntegrationEventScope> _scopes = [];

    public IReadOnlyList<ModuleHandler> Handlers => _handlers;

    /// <summary>What each delivery to a handler runs under, outermost first.</summary>
    public IReadOnlyList<IntegrationEventScope> Scopes => _scopes;

    public void AddScope(IntegrationEventScope scope) => _scopes.Add(scope);

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

        // Where every flow of work has to say who it runs as, the handlers do not inherit the system caller
        // the outbox or the transport did its bookkeeping as: they run as what the module's scopes begin.
        var required = ToolkitCallers.Required(services);
        using var none = ToolkitCallers.BeginHandler(required);

        foreach (var handler in registration.Handlers)
        {
            if (!handler.Accepts(contract))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (required && registration.Scopes.Count == 0)
            {
                // Refused before the inbox is asked anything, and recorded like any failing handler, so the
                // message is tried again once the module says what its handlers run as.
                var refusal = new NoCallerException(NoScope(handler.Consumer));
                _logger.LogError(refusal, "Consumer {Consumer} in {Module} did not run message {MessageId} ({Name}): the module says nothing about who its handlers run as.", handler.Consumer, Module, message.MessageId, message.Name);
                (failures ??= []).Add((handler.Consumer, refusal));
                continue;
            }

            inbox ??= services.GetRequiredService<DomainEventInbox<TContext>>();
            List<IDisposable>? begun = null;

            try
            {
                // Begun before the inbox's read and ended after its commit, so the read, the handler's work,
                // the save and the inbox row all run as one caller.
                foreach (var scope in registration.Scopes)
                {
                    if (scope(services, message, contract) is { } entered)
                    {
                        (begun ??= new List<IDisposable>(registration.Scopes.Count)).Add(entered);
                    }
                }

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
            finally
            {
                End(begun);
            }
        }

        return failures ?? [];
    }

    /// <summary>Ends the scopes begun for one handler, innermost first.</summary>
    private static void End(List<IDisposable>? begun)
    {
        if (begun is null)
        {
            return;
        }

        for (var i = begun.Count - 1; i >= 0; i--)
        {
            begun[i].Dispose();
        }
    }

    private string NoScope(string consumer)
        => $"The handlers of {Module} have no caller, and this host requires one (RequireExplicitCallers), so {consumer} did not run. " +
           "Say what they run as where the module registers them: module.Around((services, message, contract) => Callers.Begin(Caller.SystemIn(\"<module>\"))) " +
           "for a scoped system caller the policies hold, or module.Around(IntegrationEventScopes.System) to run them as the application itself.";
}
