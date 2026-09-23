using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// Registration of the DDDToolkit Entity Framework Core integration.
/// <code>
/// services.AddDDDToolkitEntityFramework(options =>
/// {
///     options.DispatchInProcess(async (sp, events, ct) =>
///     {
///         var publisher = sp.GetRequiredService&lt;IPublisher&gt;();
///         foreach (var e in events) await publisher.Publish(e, ct);
///     });
///     // or, for transactional delivery:
///     // options.UseOutbox(outbox => outbox.RegisterEventsFromAssemblyContaining&lt;Program&gt;());
/// });
/// services.AddDbContext&lt;MyContext&gt;((sp, db) => db.UseSqlite(cs).UseDDDToolkit(sp));
/// </code>
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the delivery options and the DDDToolkit interceptors. Pair it with
    /// <see cref="UseDDDToolkit"/> on the <c>DbContextOptionsBuilder</c>. See
    /// <see cref="DDDEntityFrameworkOptions"/> for the two delivery modes and what they guarantee.
    /// <para>
    /// Call it as often as you like. The first call registers everything; every call, the first
    /// included, configures the one <see cref="DDDEntityFrameworkOptions"/> the process has. That is how
    /// a module registers its own outbox and the contracts it reads without the host knowing:
    /// </para>
    /// <code>
    /// // host
    /// services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());
    ///
    /// // inside AddOrderingModule
    /// services.AddDDDToolkitEntityFramework(options => options.UseOutbox&lt;OrderingContext&gt;(outbox => ...));
    /// </code>
    /// </summary>
    public static IServiceCollection AddDDDToolkitEntityFramework(this IServiceCollection services, Action<DDDEntityFrameworkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = RegisteredOptions(services);
        if (options is null)
        {
            options = new DDDEntityFrameworkOptions();

            services.AddSingleton(options);
            services.AddSingleton(options.Contracts);
            // Scoped so the interceptor hands the scope's own provider (and thereby the DbContext being saved) to the handlers.
            services.TryAddScoped<PublishDomainEventsInterceptor>();
            services.TryAddSingleton<AggregateVersionInterceptor>();
            services.TryAddSingleton<InvariantInterceptor>();
        }

        configure?.Invoke(options);

        // What the outboxes send out, so a transport does not ask the broker for this process's own
        // messages: the module sink already hands those to the modules here.
        var subscriptions = services.IntegrationEventSubscriptions();
        foreach (var outbox in options.ContextOutboxes.Values.Append(options.Outbox).OfType<OutboxOptions>())
        {
            foreach (var name in outbox.PublishedNames())
            {
                subscriptions.Publishes(name);
            }
        }

        return services;
    }

    /// <summary>The options instance an earlier call registered, or <see langword="null"/> on the first call.</summary>
    private static DDDEntityFrameworkOptions? RegisteredOptions(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(DDDEntityFrameworkOptions) && !services[i].IsKeyedService)
            {
                return services[i].ImplementationInstance as DDDEntityFrameworkOptions
                    ?? throw new InvalidOperationException(
                        $"{nameof(DDDEntityFrameworkOptions)} is registered, but not as an instance, so {nameof(AddDDDToolkitEntityFramework)} cannot configure it further. " +
                        $"Register it through {nameof(AddDDDToolkitEntityFramework)} only.");
            }
        }

        return null;
    }

    /// <summary>Same as <see cref="AddDDDToolkitEntityFramework"/>; kept for readers of the 2.x API.</summary>
    public static IServiceCollection UseDomainEvents(this IServiceCollection services, Action<DDDEntityFrameworkOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddDDDToolkitEntityFramework(configure);
    }

    /// <summary>
    /// 2.x registration: dispatch in process through <paramref name="interceptorAction"/>. Equivalent to
    /// <c>AddDDDToolkitEntityFramework(o =&gt; o.DispatchInProcess(...))</c>; note that events are now
    /// dispatched before the save on both the sync and the async path.
    /// </summary>
    [Obsolete("Use AddDDDToolkitEntityFramework(options => options.DispatchInProcess((sp, events, ct) => ...)) or options.UseOutbox(...).")]
    public static IServiceCollection UseDomainEvents(this IServiceCollection services, Func<IServiceProvider, List<IDomainEvent>, Task> interceptorAction)
    {
        ArgumentNullException.ThrowIfNull(interceptorAction);
        return services.AddDDDToolkitEntityFramework(options =>
            options.DispatchInProcess((serviceProvider, events, _) => interceptorAction(serviceProvider, events.ToList())));
    }

    /// <summary>
    /// Adds every DDDToolkit interceptor to the context, in the order they run: domain event
    /// delivery (<see cref="PublishDomainEventsInterceptor"/>), then the aggregates' own invariants
    /// (<see cref="InvariantInterceptor"/>), which therefore sees whatever the handlers changed,
    /// then optimistic concurrency (<see cref="AggregateVersionInterceptor"/>), which comes last so
    /// a rejected save leaves no version bumped. Pass the provider handed to the
    /// <c>AddDbContext</c> callback so handlers resolve from the same scope as the context.
    /// </summary>
    public static DbContextOptionsBuilder UseDDDToolkit(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        optionsBuilder.AddInterceptors(
            serviceProvider.GetRequiredService<PublishDomainEventsInterceptor>(),
            serviceProvider.GetRequiredService<InvariantInterceptor>(),
            serviceProvider.GetRequiredService<AggregateVersionInterceptor>());

        return optionsBuilder;
    }

    /// <summary>Alias of <see cref="UseDDDToolkit"/>, kept for readers of the 2.x API.</summary>
    public static void AddDomainEventInterceptor(this DbContextOptionsBuilder ctx, IServiceProvider scvc) => ctx.UseDDDToolkit(scvc);

    /// <summary>
    /// Registers <see cref="OutboxProcessor{TContext}"/> (scoped) so it can be resolved and driven
    /// manually, for example from a job scheduler or a test.
    /// </summary>
    public static IServiceCollection AddOutboxProcessor<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<OutboxProcessor<TContext>>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="DomainEventInbox{TContext}"/> (scoped), the receiving side's
    /// exactly-once helper. Map its table with <c>modelBuilder.AddDomainEventInbox()</c>.
    /// </summary>
    public static IServiceCollection AddDomainEventInbox<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<DomainEventInbox<TContext>>();
        return services;
    }

    /// <summary>
    /// Registers a consuming module: the integration events it handles, each handler guarded by the
    /// inbox of <typeparamref name="TContext"/>. <see cref="ModuleIntegrationEventSink"/>, which a producing
    /// module names with <c>outbox.SendToModules()</c>, offers every message to every module registered
    /// this way.
    /// <code>
    /// services.AddModuleIntegrationEvents&lt;ShippingContext&gt;(module => module.Handle&lt;OrderPlacedV1, BookShipment&gt;());
    /// </code>
    /// <para>
    /// Register as many handlers per contract as you like; each one gets its own inbox row under its own
    /// consumer name, so they succeed and fail independently. Name them with
    /// <c>[IntegrationEventConsumer("...")]</c>, because the name is what the inbox remembers. Calling this
    /// again for the same context adds to the same module. Map the inbox table with
    /// <c>modelBuilder.AddDomainEventInbox()</c>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    public static IServiceCollection AddModuleIntegrationEvents<TContext>(this IServiceCollection services, Action<ModuleIntegrationEvents<TContext>> configure) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var registration = services
            .Where(descriptor => descriptor.ServiceType == typeof(ModuleConsumerRegistration<TContext>))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ModuleConsumerRegistration<TContext>>()
            .FirstOrDefault();

        if (registration is null)
        {
            registration = new ModuleConsumerRegistration<TContext>();

            services.AddDomainEventInbox<TContext>();
            services.AddSingleton(registration);
            services.AddScoped<IModuleIntegrationEventConsumer>(provider => new ModuleIntegrationEventConsumer<TContext>(registration, provider));
            services.TryAddScoped<ModuleIntegrationEventSink>();
            services.TryAddSingleton<IntegrationEventReceiver>();
        }

        configure(new ModuleIntegrationEvents<TContext>(services, registration, services.IntegrationEventSubscriptions()));
        return services;
    }

    /// <summary>
    /// The <see cref="Integration.IntegrationEventSubscriptions"/> of this service collection: the published
    /// names of every contract its modules handle. Registered as a singleton the first time it is asked
    /// for, and the same instance every time after, so a host can take it while configuring a transport
    /// and read it once the modules have registered.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IntegrationEventSubscriptions IntegrationEventSubscriptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var subscriptions = services
            .Where(descriptor => descriptor.ServiceType == typeof(IntegrationEventSubscriptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IntegrationEventSubscriptions>()
            .FirstOrDefault();

        if (subscriptions is null)
        {
            subscriptions = new IntegrationEventSubscriptions();
            services.AddSingleton(subscriptions);
        }

        return subscriptions;
    }

    /// <summary>
    /// Registers <see cref="OutboxBackgroundService{TContext}"/>, which polls the outbox of
    /// <typeparamref name="TContext"/> every <paramref name="pollingInterval"/> and delivers pending
    /// messages in batches of <paramref name="batchSize"/>.
    /// </summary>
    public static IServiceCollection AddOutboxBackgroundService<TContext>(this IServiceCollection services, TimeSpan pollingInterval, int batchSize = 100) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollingInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        services.AddOutboxProcessor<TContext>();
        services.TryAddSingleton(new OutboxBackgroundServiceOptions<TContext> { PollingInterval = pollingInterval, BatchSize = batchSize });
        services.AddHostedService<OutboxBackgroundService<TContext>>();
        return services;
    }
}
