using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Composition;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Connectors.InMemory;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Logging;
using HotChocolate.Fusion.Options;
using HotChocolate.Transport.Formatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// A HotChocolate Fusion gateway inside a modular monolith: it composes the source schemas the modules
/// registered into one schema, and answers GraphQL by calling them directly, in the process.
/// </summary>
/// <remarks>
/// <para>
/// Each module registers a named source schema of its own, with <c>AddGraphQLServer("catalog")</c> and
/// <c>AddSourceSchemaDefaults()</c>: its types, its queries, and its part of the types other modules own,
/// keyed on a name and a key they agree on. The gateway knows no module. It composes every schema
/// registered in the application, the way a Fusion gateway composes services across processes, so a
/// module's GraphQL is the same code in the monolith and as a service.
/// </para>
/// <para>
/// HotChocolate's own <c>AddGraphQLGatewayServer().AddInMemorySchema(...)</c> cannot serve the gateway over
/// HTTP next to more than one source schema: HotChocolate and Fusion each register themselves as the one
/// <see cref="IRequestExecutorProvider"/> of a service container, the endpoint asks it for the gateway and
/// the in-memory connector asks it for the modules, so one of the two loses. This gives the gateway a
/// service container of its own inside the application and hands it the modules explicitly, through the
/// same public classes <c>AddInMemorySchema</c> uses.
/// </para>
/// <para>
/// A source schema that cannot be composed does not fail loudly in the in-memory connector: the gateway
/// waits for a schema forever. The application's start therefore waits for the composed schema, and fails
/// as soon as the composer refuses the source schemas, with every error it logged, or when there is no
/// schema after <see cref="InMemoryFusionGatewayOptions.CompositionTimeout"/>.
/// </para>
/// </remarks>
public static class InMemoryFusionGateway
{
    /// <summary>
    /// Composes the source schemas registered in this service collection into one schema, which
    /// <see cref="MapInMemoryFusionGateway"/> serves.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType()...;
    /// builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType()...;
    /// builder.Services.AddInMemoryFusionGateway();
    ///
    /// var app = builder.Build();
    /// app.MapInMemoryFusionGateway();   // /graphql
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddInMemoryFusionGateway(
        this IServiceCollection services,
        Action<InMemoryFusionGatewayOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new InMemoryFusionGatewayOptions();
        configure?.Invoke(options);

        var gateway = new GatewayRegistration(options);
        services.AddSingleton(gateway);

        // The gateway resolves its clients for the source schemas from the request's services, which are
        // this container's; this container hands that one service over from the gateway's.
        services.AddSingleton(_ => gateway.RequireServices().GetRequiredService<ISourceSchemaClientScopeFactory>());

        services.AddHostedService(_ => new CompositionCheck(gateway));

        // The schemas as text, for a test that compares each with a committed file.
        services.AddSingleton(application => new InMemoryFusionSchemas(application, gateway.ComposedAsync, options.ServedApart));

        return services;
    }

    /// <summary>Serves the composed schema at <paramref name="path"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AddInMemoryFusionGateway"/> was not called, no source schema is registered, or
    /// <see cref="InMemoryFusionGatewayOptions.ServedApart"/> names a schema the application does not register.
    /// </exception>
    public static WebApplication MapInMemoryFusionGateway(this WebApplication app, string path = "/graphql")
    {
        ArgumentNullException.ThrowIfNull(app);

        var gateway = app.Services.GetService<GatewayRegistration>()
            ?? throw new InvalidOperationException("Call AddInMemoryFusionGateway() on the services first.");

        // Every schema in the application's container is a source schema to compose, but those it serves apart. A
        // name served apart that no schema has leaves out nothing: the schema it was meant for, renamed or spelled
        // otherwise, would be composed, and its fields offered here. So it fails the start instead.
        var sourceSchemas = app.Services.GetService<IRequestExecutorProvider>();
        var registered = sourceSchemas?.SchemaNames ?? [];
        var unknown = gateway.Options.ServedApart.Where(name => !registered.Contains(name, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                "ServedApart names " + string.Join(", ", unknown.Select(name => "'" + name + "'"))
                + ", which no schema of the application is registered under, so the gateway would leave out nothing it was meant to."
                + " The schemas registered are " + (registered.Length == 0 ? "none" : string.Join(", ", registered.Order(StringComparer.Ordinal).Select(name => "'" + name + "'")))
                + ": name each one as AddGraphQLServer(\"name\") does, in the same case.");
        }

        var names = SourceSchemaNames(sourceSchemas, gateway.Options.ServedApart);
        if (sourceSchemas is null || names.Length == 0)
        {
            throw new InvalidOperationException(registered.Length > 0
                ? "Every schema of the application is served apart, so the gateway has no source schema to compose. Register each module's with AddGraphQLServer(\"name\").AddSourceSchemaDefaults(), and leave it out of ServedApart."
                : "No source schema is registered. Register each module's with AddGraphQLServer(\"name\").AddSourceSchemaDefaults().");
        }

        var sourceSchemaEvents = app.Services.GetRequiredService<IRequestExecutorEvents>();

        var composition = new SchemaComposerOptions();
        // Relay across the source schemas: node(id:) answered by whichever one owns the type in the id.
        composition.Merger.EnableGlobalObjectIdentification = gateway.Options.EnableGlobalObjectIdentification;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();

        var builder = services
            .AddGraphQLGatewayServer()
            .AddConfigurationProvider(_ =>
            {
                // The connector starts composing in its constructor, and tells only who is listening at that
                // moment that a composition failed: it keeps nothing, and it does not try again. Source schemas
                // that are built without waiting for anything would be composed, and refused, before anybody
                // could subscribe. So the composer is handed no source schema until the listener is there.
                var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var provider = new InMemoryConfigurationProvider(names, new OnceListening(sourceSchemas, names, listening.Task), sourceSchemaEvents, composition);

                try
                {
                    provider.Subscribe(new CompositionErrors(gateway));
                }
                finally
                {
                    listening.SetResult();
                }

                return provider;
            });

        // The connector's clients, with the requests of a batch kept apart where the connector mixes them up.
        services.AddSingleton<ISourceSchemaClientFactory>(
            new RequestsKeptApart(new InMemorySourceSchemaClientFactory(sourceSchemas, sourceSchemaEvents, JsonResultFormatter.Default)));

        foreach (var name in names)
        {
            FusionSetupUtilities.Configure(builder,
                setup => setup.ClientConfigurationModifiers.Add(_ => new InMemorySourceSchemaClientConfiguration(name)));
        }

        gateway.Options.ConfigureGateway?.Invoke(builder);

        var gatewayServices = services.BuildServiceProvider();
        gateway.Services = gatewayServices;

        app.UseWhen(context => context.Request.Path.StartsWithSegments(path), branch =>
        {
            branch.ApplicationServices = gatewayServices;
            branch.MapGraphQL(path, "_Default");
        });

        return app;
    }

    /// <summary>
    /// The schemas the gateway composes: every schema of the application but those it serves apart, in the order
    /// the application lists them.
    /// </summary>
    internal static string[] SourceSchemaNames(IRequestExecutorProvider? schemas, IEnumerable<string> servedApart)
        => schemas is null ? [] : [.. schemas.SchemaNames.Except(servedApart, StringComparer.Ordinal)];

    /// <summary>What the two halves share: the options, and the gateway's container once it is built.</summary>
    private sealed class GatewayRegistration(InMemoryFusionGatewayOptions options)
    {
        public InMemoryFusionGatewayOptions Options { get; } = options;

        private readonly TaskCompletionSource _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IServiceProvider? Services { get; set; }

        public Exception? CompositionError { get; private set; }

        /// <summary>The composer refused the source schemas. It does not compose again, so whoever waits can stop.</summary>
        public void Failed(Exception error)
        {
            CompositionError = error;
            _failed.TrySetResult();
        }

        /// <summary>The source schemas composed, so an error of an earlier attempt no longer describes them.</summary>
        public void Composed() => CompositionError = null;

        public IServiceProvider RequireServices()
            => Services ?? throw new InvalidOperationException("Call MapInMemoryFusionGateway() on the application.");

        /// <summary>
        /// The gateway's executor once the source schemas are composed. A composition that fails does not
        /// always fail the request for the executor: when the gateway was not listening yet, it waits for a
        /// schema forever. So this stops waiting when the composer said no, or after
        /// <see cref="InMemoryFusionGatewayOptions.CompositionTimeout"/>, and says what is known.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// <see cref="MapInMemoryFusionGateway"/> was not called, or there is no composed schema.
        /// </exception>
        public async Task<IRequestExecutor> ComposedAsync(CancellationToken cancellationToken)
        {
            var executors = RequireServices().GetRequiredService<IRequestExecutorProvider>();
            var composing = executors.GetExecutorAsync(cancellationToken: cancellationToken).AsTask();
            var timeout = Options.CompositionTimeout;

            if (await Task.WhenAny(composing, _failed.Task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false) == composing)
            {
                try
                {
                    return await composing.ConfigureAwait(false);
                }
                catch (SchemaCompositionException refused)
                {
                    // The gateway was listening too, and heard the composer a moment before this did. It is
                    // the same refusal, so it reads the same whichever of the two got there first.
                    throw Refused(CompositionError ?? refused);
                }
            }

            // The request for the executor is given up on. If the gateway heard the composer as well, that
            // request fails later, and nobody is left to look at how.
            _ = composing.ContinueWith(
                static abandoned => _ = abandoned.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            // Whoever stopped waiting gets a cancellation, not a story about composition.
            cancellationToken.ThrowIfCancellationRequested();

            throw CompositionError is { } error
                ? Refused(error)
                : new InvalidOperationException(
                    $"The source schemas were not composed into one within {timeout.TotalSeconds:0} seconds. "
                    + "Composition failures are not always reported: check that every source schema's root query "
                    + "type is called Query, that a type two schemas both declare shares its key, and that fields "
                    + "several schemas return are @shareable.");
        }

        /// <summary>The composer's refusal as the application's start and a printed schema report it, with the composer's own exception inside.</summary>
        private static InvalidOperationException Refused(Exception error)
            => new("The source schemas could not be composed into one: " + Describe(error), error);

        /// <summary>Every error the composer logged, where its own message names the first one only.</summary>
        private static string Describe(Exception error)
        {
            if (error is not SchemaCompositionException { CompositionLog: { } log })
            {
                return error.Message;
            }

            var errors = log.Where(static entry => entry.Severity == LogSeverity.Error).Select(static entry => entry.Message).ToArray();
            return errors.Length switch
            {
                0 => error.Message,
                1 => errors[0],
                _ => errors.Length + " errors." + string.Concat(errors.Select(static message => Environment.NewLine + "  - " + message)),
            };
        }
    }

    /// <summary>Keeps the composition error the connector reports and forgets.</summary>
    private sealed class CompositionErrors(GatewayRegistration gateway) : IObserver<FusionConfiguration>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => gateway.Failed(error);

        public void OnNext(FusionConfiguration value) => gateway.Composed();
    }

    /// <summary>
    /// The application's source schemas, held back until whoever keeps the composer's errors is listening:
    /// the composer asks for them from its constructor, and is answered once <paramref name="listening"/>
    /// completes, however fast a schema is built. After that it is the application's provider and nothing more,
    /// with the schemas it serves apart left out of its names.
    /// </summary>
    private sealed class OnceListening(IRequestExecutorProvider sourceSchemas, string[] names, Task listening) : IRequestExecutorProvider
    {
        public ImmutableArray<string> SchemaNames { get; } = [.. names];

        public ValueTask<IRequestExecutor> GetExecutorAsync(string? schemaName = null, CancellationToken cancellationToken = default)
            => listening.IsCompletedSuccessfully
                ? sourceSchemas.GetExecutorAsync(schemaName, cancellationToken)
                : WaitAsync(schemaName, cancellationToken);

        private async ValueTask<IRequestExecutor> WaitAsync(string? schemaName, CancellationToken cancellationToken)
        {
            await listening.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await sourceSchemas.GetExecutorAsync(schemaName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Holds the application's start until the gateway has a composed schema, and fails it, naming the
    /// problem where it can, when there is none in time.
    /// </summary>
    private sealed class CompositionCheck(GatewayRegistration gateway) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => gateway.ComposedAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>Options for <see cref="InMemoryFusionGateway.AddInMemoryFusionGateway"/>.</summary>
public sealed class InMemoryFusionGatewayOptions
{
    /// <summary>
    /// How long the application's start waits for the composed schema at most. Thirty seconds by default. A
    /// composition the composer refuses fails the start at once, without waiting this long.
    /// </summary>
    /// <remarks>
    /// It bounds the gateway's own wait, and nothing that comes before it. HotChocolate builds every source schema
    /// when the application starts, in a hosted service that is registered before the gateway's and has no
    /// timeout of its own: a source schema whose building never ends, one that waits in
    /// <c>ConfigureSchemaAsync</c> for something that does not come, holds the start there, however short this is.
    /// </remarks>
    public TimeSpan CompositionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether <c>node(id:)</c> spans the source schemas, answered by whichever one owns the type in the id.
    /// On by default.
    /// </summary>
    public bool EnableGlobalObjectIdentification { get; set; } = true;

    /// <summary>Anything else for the gateway, such as <c>ModifyRequestOptions(...)</c>.</summary>
    public Action<IFusionGatewayBuilder>? ConfigureGateway { get; set; }

    /// <summary>
    /// The schemas of the application that the gateway leaves out, by name: each one the application serves on its
    /// own, beside the gateway, at an endpoint of its own. Empty by default, and the gateway composes every schema
    /// the application registers.
    /// </summary>
    /// <remarks>
    /// For a schema with fields the gateway's clients must not be offered, such as an administration schema the
    /// host maps with <c>MapGraphQL("/admin/graphql", "admin")</c>. Composed, its fields would be the gateway's too.
    /// A name here that no schema is registered under fails <c>MapInMemoryFusionGateway()</c>: it would leave out
    /// nothing, and the schema it was meant for would be composed. Names are compared as they are written. The
    /// gateway's own options, <see cref="ConfigureGateway"/> among them, do not reach such a schema: it bounds a
    /// request itself.
    /// </remarks>
    /// <example>
    /// <code>
    /// const string Administration = "admin";   // one name, for the schema, the gateway and the endpoint
    ///
    /// builder.Services.AddGraphQLServer(Administration).AddTenantsGraphQlRuntimeBindings().AddTenantsTypes();
    /// builder.Services.AddInMemoryFusionGateway(options => options.ServedApart.Add(Administration));
    ///
    /// app.MapInMemoryFusionGateway();                          // /graphql: every other schema, composed
    /// app.MapGraphQL("/admin/graphql", Administration);        // the administration's, on its own
    /// </code>
    /// </example>
    public ISet<string> ServedApart { get; } = new HashSet<string>(StringComparer.Ordinal);
}
