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
    /// </summary>
    public static IServiceCollection AddDDDToolkitEntityFramework(this IServiceCollection services, Action<DDDEntityFrameworkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DDDEntityFrameworkOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton(options.Contracts);
        // Scoped so the interceptor hands the scope's own provider (and thereby the DbContext being saved) to the handlers.
        services.TryAddScoped<PublishDomainEventsInterceptor>();
        services.TryAddSingleton<AggregateVersionInterceptor>();

        if (options.Outbox is { } outbox)
        {
            services.TryAddSingleton(outbox.EventTypes);
        }

        return services;
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
    /// Adds every DDDToolkit interceptor to the context: domain event delivery
    /// (<see cref="PublishDomainEventsInterceptor"/>) followed by optimistic concurrency
    /// (<see cref="AggregateVersionInterceptor"/>). Pass the provider handed to the
    /// <c>AddDbContext</c> callback so handlers resolve from the same scope as the context.
    /// </summary>
    public static DbContextOptionsBuilder UseDDDToolkit(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        optionsBuilder.AddInterceptors(
            serviceProvider.GetRequiredService<PublishDomainEventsInterceptor>(),
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
    /// Registers <typeparamref name="THandler"/> (scoped) as a consumer of
    /// <typeparamref name="TContract"/>, so <see cref="ModuleIntegrationEventSink{TContext}"/> hands it
    /// every message published under that contract.
    /// <para>
    /// Register as many handlers per contract as you like; each one gets its own inbox row under its own
    /// consumer name, so they succeed and fail independently. Name them with
    /// <c>[IntegrationEventConsumer("...")]</c>, because the name is what the inbox remembers.
    /// </para>
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TContract, THandler>(this IServiceCollection services)
        where TContract : class
        where THandler : class, IIntegrationEventHandler<TContract>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IIntegrationEventHandler<TContract>, THandler>();
        return services;
    }

    /// <summary>
    /// Registers <paramref name="handler"/> as a consumer of <typeparamref name="TContract"/>, for an
    /// instance you already own. Tests usually want this one.
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TContract>(this IServiceCollection services, IIntegrationEventHandler<TContract> handler)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handler);
        services.AddSingleton(handler);
        return services;
    }

    /// <summary>
    /// Registers <see cref="ModuleIntegrationEventSink{TContext}"/> (scoped) together with the inbox it
    /// needs. Name it as a sink with <c>outbox.SendToModules&lt;TContext&gt;()</c>, and map the inbox
    /// table with <c>modelBuilder.AddDomainEventInbox()</c>.
    /// </summary>
    public static IServiceCollection AddModuleIntegrationEvents<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDomainEventInbox<TContext>();
        services.TryAddScoped<ModuleIntegrationEventSink<TContext>>();
        return services;
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
