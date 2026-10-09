using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.Interfaces;
using DDDToolkit.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    /// <para>
    /// It brings the start-up check <see cref="EntityFrameworkChecks.ToolkitWiredCheck"/>, which a host runs with
    /// <c>services.RunStartupChecks()</c>: every registered context that maps the toolkit's classes is wired
    /// through <see cref="UseDDDToolkit"/>.
    /// </para>
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
            // For a host that adds the interceptor by hand: scoped, so it hands the scope's own provider (and
            // thereby the DbContext being saved) to the handlers. UseDDDToolkit builds its own instead, so the
            // same call works in the options callback of a context pool, which is handed the root provider.
            services.TryAddScoped<PublishDomainEventsInterceptor>();
            // How that interceptor tells a scope's provider from the root one before a handler gets it.
            services.TryAddScoped<ScopeMarker>();
            services.TryAddSingleton<AggregateVersionInterceptor>();
            services.TryAddSingleton<InvariantInterceptor>();
            services.TryAddSingleton<DatabaseRefusalInterceptor>();
            // Who is acting, which an event log writes on every row: the toolkit's own caller, unless a package
            // or the host registered an accessor that knows more. A singleton, asked once per save.
            services.TryAddSingleton<IActedByAccessor, CallerActedByAccessor>();

            // Which contexts UseDDDToolkit gave the parts the packages bring, so each is named once in the log.
            services.TryAddSingleton<ContextParts>();

            // Every context saves through the interceptors above, or the host does not start, once it runs its checks.
            services.AddStartupCheck(EntityFrameworkChecks.ToolkitWired);
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
    /// Wires the context for the toolkit, with one call: the toolkit's own interceptors
    /// (<see cref="UseDDDToolkitCore"/>), then every part the registered packages bring for a context's options, in
    /// their order. Row level security, which <c>AddPostgresRowLevelSecurity</c> and
    /// <c>AddSupabaseRowLevelSecurity</c> bring, goes on a context that may be on Postgres; Tenancy's save check,
    /// which <c>AddTenancy</c> brings, last, on every context, where it checks the saves of the ones that keep rows
    /// to a tenant. A host that registered none of them gets the toolkit's interceptors and nothing else.
    /// <code>
    /// services.AddDbContext&lt;OrderingContext&gt;((services, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(services));
    /// </code>
    /// <para>
    /// Whether a part belongs on the context is worked out from what the options hold when this is called: row level
    /// security is left off a context whose provider is configured and is not Postgres's. What the options cannot
    /// say yet, a part decides when it is used: row level security passes over a context whose provider, configured
    /// after this call or in <c>OnConfiguring</c>, turns out not to be Postgres's, and Tenancy's save check over one
    /// whose model keeps no rows to a tenant. A model that cannot do without a part says so, and a context whose
    /// options lack it is refused at its first save (<see cref="ContextPartRequirements"/>).
    /// </para>
    /// <para>
    /// A context that should do without a part the host registered is configured with
    /// <see cref="UseDDDToolkitCore"/> and the <c>Use...</c> calls of the parts it does want. Those calls stay, and
    /// add nothing the options already have, so a chain written out in full,
    /// <c>UseDDDToolkit(provider).UseSupabaseRowLevelSecurity(provider).UseTenancy(provider)</c>, still gives each
    /// interceptor once. The first time the options of a context type are built with any part, an information line
    /// names the context and the parts: up to 3.1 this call added the toolkit's interceptors alone, and a context that now runs as its
    /// caller, say, is named where the log says what it was given.
    /// </para>
    /// <para>
    /// Pass the provider handed to the options callback: of <c>AddDbContext</c>, a scope's, so handlers
    /// resolve from the same scope as the context; of a context pool (<c>AddPooledDbContextFactory</c>,
    /// <c>AddDbContextPool</c>), the application's root provider, because a pool builds its options once.
    /// Under a pool the handlers get the scope a context was rented in instead, which
    /// <see cref="PooledContexts.AddScopedFromPool{TContext}"/> and
    /// <see cref="PooledContexts.BindToScope{TContext}"/> name. Nothing scoped is resolved here, nor by the parts
    /// of the toolkit's packages, so the call is the same for every registration.
    /// </para>
    /// <para>
    /// The context's migration history goes in its default schema, beside its tables, so the modules that share a
    /// database each keep a history of their own: <c>ordering."__EFMigrationsHistory"</c> for a model with
    /// <c>HasDefaultSchema("ordering")</c>. Options that name a history table, with <c>MigrationsHistoryTable</c>
    /// before or after this call, keep it where they name it; a model without a default schema keeps the provider's.
    /// A design-time factory, which has no services to hand this call, gives its context the same history with
    /// <see cref="UseDDDToolkitDesignTime(DbContextOptionsBuilder)"/>.
    /// </para>
    /// </summary>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <param name="serviceProvider">The provider handed to the options callback, of <c>AddDbContext</c> or of a context pool.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="AddDDDToolkitEntityFramework"/> was not called.</exception>
    public static DbContextOptionsBuilder UseDDDToolkit(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        AddToolkitBase(optionsBuilder, serviceProvider);
        ContextParts.Apply(optionsBuilder, serviceProvider);
        return optionsBuilder;
    }

    /// <summary>
    /// Adds the toolkit's own interceptors to the context, and its migration history in the context's default schema,
    /// and nothing a package brings: what <see cref="UseDDDToolkit"/> did up to 3.1, with the history where
    /// <see cref="UseDDDToolkit"/> keeps it now. In the order they run: domain event delivery
    /// (<see cref="PublishDomainEventsInterceptor"/>), then the aggregates' own invariants
    /// (<see cref="InvariantInterceptor"/>), which therefore sees whatever the handlers changed,
    /// then optimistic concurrency (<see cref="AggregateVersionInterceptor"/>), which comes after them so
    /// a rejected save leaves no version bumped, and last <see cref="DatabaseRefusalInterceptor"/>, which
    /// only answers a save the database refused: the refusal a unique index declares, or
    /// <c>access.refused</c> for a row a policy denied.
    /// <para>
    /// It is for a context that should do without a part the host registered: one on Postgres that runs as the
    /// role the application logged in as while the others run as their caller, say. Such a context takes the parts
    /// it does want by their own calls, after this one:
    /// </para>
    /// <code>
    /// options.UseNpgsql(connectionString).UseDDDToolkitCore(services).UseTenancy(services);
    /// </code>
    /// <para>
    /// Writing it is the decision, and the options keep it: the start-up check of row level security takes a context
    /// on Postgres that was given the base alone with this call, and no row level security, as one that runs as the
    /// login role on purpose. A context that added the toolkit's interceptors by hand says nothing of the kind, and is
    /// still refused. A part the context's model cannot do without is still held to: Tenancy's save check, for a
    /// context that keeps rows to a tenant (<see cref="ContextPartRequirements"/>).
    /// </para>
    /// <para>
    /// The migration history is the base's, not a package's: a context given the base alone keeps it in its default
    /// schema as <see cref="UseDDDToolkit"/> does, so moving a context from the one call to the other leaves its
    /// history where it is.
    /// </para>
    /// <para>
    /// It adds the interceptors the options do not have yet, so a second call adds nothing. The provider is the
    /// one <see cref="UseDDDToolkit"/> is handed, for the same reasons.
    /// </para>
    /// </summary>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <param name="serviceProvider">The provider handed to the options callback, of <c>AddDbContext</c> or of a context pool.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="AddDDDToolkitEntityFramework"/> was not called.</exception>
    public static DbContextOptionsBuilder UseDDDToolkitCore(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        AddToolkitBase(optionsBuilder, serviceProvider);

        // What the call says, kept where a check can read it: the base alone, on purpose.
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(BaseAloneExtension.Instance);
        return optionsBuilder;
    }

    /// <summary>
    /// What a design-time factory gives its context of the toolkit: what <see cref="UseDDDToolkit"/> adds that needs
    /// no service, which is the migration history in the context's default schema. <c>dotnet ef</c> and the Supabase
    /// export make the context through that factory, before any host exists, so they cannot hand it the application's
    /// services; with this call the scripts they write record the migrations in the table the running application
    /// reads.
    /// <code>
    /// public sealed class OrderingContextFactory : IDesignTimeDbContextFactory&lt;OrderingContext&gt;
    /// {
    ///     public OrderingContext CreateDbContext(string[] args)
    ///         => new(new DbContextOptionsBuilder&lt;OrderingContext&gt;().UseSqlServer("Server=unused").UseDDDToolkitDesignTime().Options);
    /// }
    /// </code>
    /// <para>
    /// A context marked <c>[SupabaseMigrations]</c> needs no such factory written by hand: the Supabase package's
    /// generator writes one beside it, on Npgsql and with this call.
    /// </para>
    /// <para>
    /// Without it the factory's context keeps the history in the provider's default schema, <c>public</c> on Postgres,
    /// while the running one, wired with <see cref="UseDDDToolkit"/>, keeps it in the model's: <c>dotnet ef database
    /// update</c> and the exported files would record every migration where the application does not look, which the
    /// build reports as DDD00074 at the factory's <c>CreateDbContext</c>. A host's
    /// options may call it too, and it adds nothing they already have, so options shared by the host and the factory
    /// are written once. It adds no interceptor: a design-time context saves nothing.
    /// </para>
    /// </summary>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is null.</exception>
    public static DbContextOptionsBuilder UseDDDToolkitDesignTime(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(MigrationHistoryExtension.Instance);
        return optionsBuilder;
    }

    /// <summary>
    /// <see cref="UseDDDToolkitDesignTime(DbContextOptionsBuilder)"/> for the options of one context type, so a factory
    /// takes their <c>Options</c> in the same expression and hands them to the context's constructor.
    /// </summary>
    /// <typeparam name="TContext">The context.</typeparam>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is null.</exception>
    public static DbContextOptionsBuilder<TContext> UseDDDToolkitDesignTime<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDDDToolkitDesignTime((DbContextOptionsBuilder)optionsBuilder);

    /// <summary>
    /// What both <see cref="UseDDDToolkit"/> and <see cref="UseDDDToolkitCore"/> start with: the toolkit's own
    /// interceptors, and the migration history in the context's default schema.
    /// </summary>
    private static void AddToolkitBase(DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        AddToolkitInterceptors(optionsBuilder, serviceProvider);
        UseDDDToolkitDesignTime(optionsBuilder);
    }

    /// <summary>
    /// Adds the toolkit's own interceptors the options do not have yet, in the order they run.
    /// </summary>
    private static void AddToolkitInterceptors(DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        var present = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        List<IInterceptor> missing = [];

        if (!present.OfType<PublishDomainEventsInterceptor>().Any())
        {
            // Built, not resolved: the registration is scoped, and a pool's callback has no scope to resolve it from.
            missing.Add(new PublishDomainEventsInterceptor(serviceProvider, serviceProvider.GetRequiredService<DDDEntityFrameworkOptions>()));
        }

        AddUnlessPresent<InvariantInterceptor>(present, missing, serviceProvider);
        AddUnlessPresent<AggregateVersionInterceptor>(present, missing, serviceProvider);
        AddUnlessPresent<DatabaseRefusalInterceptor>(present, missing, serviceProvider);

        if (missing.Count > 0)
        {
            optionsBuilder.AddInterceptors(missing);
        }
    }

    /// <summary>Alias of <see cref="UseDDDToolkit"/>, kept for readers of the 2.x API.</summary>
    public static void AddDomainEventInterceptor(this DbContextOptionsBuilder ctx, IServiceProvider scvc) => ctx.UseDDDToolkit(scvc);

    /// <summary>Adds the registered <typeparamref name="TInterceptor"/> to <paramref name="missing"/> unless <paramref name="present"/> holds one.</summary>
    private static void AddUnlessPresent<TInterceptor>(IEnumerable<IInterceptor> present, List<IInterceptor> missing, IServiceProvider serviceProvider)
        where TInterceptor : class, IInterceptor
    {
        if (!present.OfType<TInterceptor>().Any())
        {
            missing.Add(serviceProvider.GetRequiredService<TInterceptor>());
        }
    }

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

    /// <summary>
    /// Deletes the outbox, inbox and event log rows of <typeparamref name="TContext"/> once they are older
    /// than you want to keep them, every <see cref="DomainEventRetentionOptions{TContext}.Interval"/>:
    /// <code>
    /// services.AddDomainEventRetention&lt;OrderingContext&gt;(retention =&gt;
    /// {
    ///     retention.KeepOutboxFor = TimeSpan.FromDays(7);
    ///     retention.KeepInboxFor = TimeSpan.FromDays(30);
    ///     retention.KeepEventLogFor = TimeSpan.FromDays(365);
    /// });
    /// </code>
    /// Registers <see cref="DomainEventRetention{TContext}"/> (scoped) and
    /// <see cref="DomainEventRetentionService{TContext}"/>. Calling it again for the same context
    /// configures the same options, so a module can add its own window to a host's. Each table has a window
    /// of its own and one without is left alone, so the outbox's says nothing about the event log.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException">No window is set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A window or the interval is not positive, or the batch size is below 1.</exception>
    public static IServiceCollection AddDomainEventRetention<TContext>(this IServiceCollection services, Action<DomainEventRetentionOptions<TContext>> configure) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(DomainEventRetentionOptions<TContext>))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DomainEventRetentionOptions<TContext>>()
            .FirstOrDefault();

        if (options is null)
        {
            options = new DomainEventRetentionOptions<TContext>();
            services.AddSingleton(options);
        }

        configure(options);
        options.Validate(nameof(configure));

        services.TryAddScoped<DomainEventRetention<TContext>>();
        services.AddHostedService<DomainEventRetentionService<TContext>>();
        return services;
    }
}
