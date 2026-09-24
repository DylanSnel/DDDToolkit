using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Connectors.InMemory;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Options;
using HotChocolate.Transport.Formatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.GraphQL;

/// <summary>
/// A Fusion gateway inside a modular monolith: it composes the source schemas of the modules in the
/// process into one schema, and answers <c>/graphql</c> by calling them directly, with no HTTP.
/// </summary>
/// <remarks>
/// It knows no module. Each module declares its own source schema in its presentation layer, and
/// registers it with the rest of the module when the host serves GraphQL
/// (<c>ModuleHost.WithGraphQL</c>): its types, its queries, and its part of the types other modules own,
/// keyed on a name and a key they agree on. The gateway composes every source schema registered in the
/// application, the way the Fusion gateway of the microservices samples does across processes.
/// <para>
/// The gateway has a service container of its own, inside the application. HotChocolate and Fusion each
/// register themselves as the one <see cref="IRequestExecutorProvider"/> of a container: the endpoint asks
/// it for the gateway, and HotChocolate's in-memory connector asks it for the modules, so in one container
/// one of the two loses. The modules stay in the application's container; the gateway gets its own and is
/// handed the modules explicitly, through the same public classes <c>AddInMemorySchema</c> uses. See
/// <c>Tests/Spikes/DDDToolkit.Spikes.FusionInProcess</c>.
/// </para>
/// </remarks>
public static class ModuleGateway
{
    /// <summary>The gateway's container, once it is built.</summary>
    private sealed class Registration
    {
        public IServiceProvider? Services { get; set; }
    }

    /// <summary>
    /// Composes the source schemas the modules registered in this application into one schema.
    /// <see cref="MapModuleGateway"/> serves it.
    /// </summary>
    public static IServiceCollection AddModuleGateway(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registration = new Registration();
        services.AddSingleton(registration);

        // The gateway resolves its clients for the modules from the request's services, which are this
        // container's; this container hands that one service over from the gateway's.
        services.AddSingleton(_ => (registration.Services ?? throw new InvalidOperationException("Call MapModuleGateway."))
            .GetRequiredService<ISourceSchemaClientScopeFactory>());

        return services;
    }

    /// <summary>The composed schema, at <c>/graphql</c>.</summary>
    public static WebApplication MapModuleGateway(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var registration = app.Services.GetRequiredService<Registration>();
        var modules = app.Services.GetRequiredService<IRequestExecutorProvider>();
        var moduleEvents = app.Services.GetRequiredService<IRequestExecutorEvents>();

        // Every schema in the application's container is a module's source schema.
        var sourceSchemas = modules.SchemaNames.ToArray();
        if (sourceSchemas.Length == 0)
        {
            throw new InvalidOperationException("No module registered a source schema. Give the modules a host WithGraphQL(...).");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();

        var composition = new SchemaComposerOptions();
        // Relay across the modules: node(id:) is answered by whichever module owns the type in the id.
        composition.Merger.EnableGlobalObjectIdentification = true;

        var gateway = services
            .AddGraphQLGatewayServer()
            .AddConfigurationProvider(_ => new InMemoryConfigurationProvider(sourceSchemas, modules, moduleEvents, composition));

        services.AddSingleton<ISourceSchemaClientFactory>(
            new InMemorySourceSchemaClientFactory(modules, moduleEvents, JsonResultFormatter.Default));

        foreach (var sourceSchema in sourceSchemas)
        {
            FusionSetupUtilities.Configure(gateway,
                setup => setup.ClientConfigurationModifiers.Add(_ => new InMemorySourceSchemaClientConfiguration(sourceSchema)));
        }

        var provider = services.BuildServiceProvider();
        registration.Services = provider;

        app.UseWhen(context => context.Request.Path.StartsWithSegments("/graphql"), branch =>
        {
            branch.ApplicationServices = provider;
            branch.MapGraphQL("/graphql", "_Default");
        });

        return app;
    }
}
