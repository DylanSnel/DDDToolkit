using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Connectors.InMemory;
using HotChocolate.Fusion.Execution.Clients;
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
/// waits for a schema forever. The application's start therefore waits for the composed schema, and fails,
/// naming the problem where it can, when there is none after
/// <see cref="InMemoryFusionGatewayOptions.CompositionTimeout"/>.
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

        return services;
    }

    /// <summary>Serves the composed schema at <paramref name="path"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AddInMemoryFusionGateway"/> was not called, or no source schema is registered.
    /// </exception>
    public static WebApplication MapInMemoryFusionGateway(this WebApplication app, string path = "/graphql")
    {
        ArgumentNullException.ThrowIfNull(app);

        var gateway = app.Services.GetService<GatewayRegistration>()
            ?? throw new InvalidOperationException("Call AddInMemoryFusionGateway() on the services first.");

        // Every schema in the application's container is a source schema to compose.
        var sourceSchemas = app.Services.GetService<IRequestExecutorProvider>();
        var names = sourceSchemas?.SchemaNames.ToArray() ?? [];
        if (sourceSchemas is null || names.Length == 0)
        {
            throw new InvalidOperationException(
                "No source schema is registered. Register each module's with AddGraphQLServer(\"name\").AddSourceSchemaDefaults().");
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
                var provider = new InMemoryConfigurationProvider(names, sourceSchemas, sourceSchemaEvents, composition);

                // The connector tells only who is listening that a composition failed, and keeps nothing.
                provider.Subscribe(new CompositionErrors(gateway));
                return provider;
            });

        services.AddSingleton<ISourceSchemaClientFactory>(
            new InMemorySourceSchemaClientFactory(sourceSchemas, sourceSchemaEvents, JsonResultFormatter.Default));

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

    /// <summary>What the two halves share: the options, and the gateway's container once it is built.</summary>
    private sealed class GatewayRegistration(InMemoryFusionGatewayOptions options)
    {
        public InMemoryFusionGatewayOptions Options { get; } = options;

        public IServiceProvider? Services { get; set; }

        public Exception? CompositionError { get; set; }

        public IServiceProvider RequireServices()
            => Services ?? throw new InvalidOperationException("Call MapInMemoryFusionGateway() on the application.");
    }

    /// <summary>Keeps the composition error the connector reports and forgets.</summary>
    private sealed class CompositionErrors(GatewayRegistration gateway) : IObserver<FusionConfiguration>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => gateway.CompositionError = error;

        public void OnNext(FusionConfiguration value) => gateway.CompositionError = null;
    }

    /// <summary>
    /// Holds the application's start until the gateway has a composed schema, and fails it, naming the
    /// problem where it can, when there is none in time.
    /// </summary>
    private sealed class CompositionCheck(GatewayRegistration gateway) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var executors = gateway.RequireServices().GetRequiredService<IRequestExecutorProvider>();
            var composing = executors.GetExecutorAsync(cancellationToken: cancellationToken).AsTask();
            var timeout = gateway.Options.CompositionTimeout;

            if (await Task.WhenAny(composing, Task.Delay(timeout, cancellationToken)) != composing)
            {
                throw new InvalidOperationException(gateway.CompositionError is { } error
                    ? "The source schemas could not be composed into one: " + error.Message
                    : $"The source schemas were not composed into one within {timeout.TotalSeconds:0} seconds. "
                        + "Composition failures are not always reported: check that every source schema's root query "
                        + "type is called Query, that a type two schemas both declare shares its key, and that fields "
                        + "several schemas return are @shareable.",
                    gateway.CompositionError);
            }

            await composing;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>Options for <see cref="InMemoryFusionGateway.AddInMemoryFusionGateway"/>.</summary>
public sealed class InMemoryFusionGatewayOptions
{
    /// <summary>
    /// How long the application's start waits for the composed schema. Thirty seconds by default.
    /// </summary>
    public TimeSpan CompositionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether <c>node(id:)</c> spans the source schemas, answered by whichever one owns the type in the id.
    /// On by default.
    /// </summary>
    public bool EnableGlobalObjectIdentification { get; set; } = true;

    /// <summary>Anything else for the gateway, such as <c>ModifyRequestOptions(...)</c>.</summary>
    public Action<IFusionGatewayBuilder>? ConfigureGateway { get; set; }
}
