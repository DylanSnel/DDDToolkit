using DDDToolkit.Examples.Catalog.Api.GraphQL;
using DDDToolkit.Examples.Inventory.Api.GraphQL;
using DDDToolkit.Examples.Ordering.Api.GraphQL;
using DDDToolkit.Examples.Payments.Api.GraphQL;
using DDDToolkit.Examples.Shipping.Api.GraphQL;
using DDDToolkit.HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
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
/// The shop as one GraphQL schema over five modules, composed by Fusion inside the monolith.
/// </summary>
/// <remarks>
/// A client asks
/// <code>
/// { order(id: "…") { status lines { quantity product { name price { amount } } } payment { status } shipment { destination } } }
/// </code>
/// and has no idea that five modules answered. Each module serves a source schema of its own and declares
/// its own part of a type another module owns: Ordering says a line's product is the <c>Product</c> with
/// that SKU, Payments and Shipping say what they add to the <c>Order</c> with that id. None of them knows
/// the others' classes; they agree on a type's name and its key.
/// <para>
/// A Fusion gateway composes the five when the application starts and answers every query by calling
/// the modules' schemas directly, in this process. The microservices samples compose the very same source
/// schemas across processes, so a module's GraphQL is the same code however the module is hosted.
/// </para>
/// <para>
/// The gateway has a service container of its own, inside this application. HotChocolate and Fusion each
/// register themselves as the one <see cref="IRequestExecutorProvider"/> of a container: the endpoint asks
/// it for the gateway, and HotChocolate's in-memory connector asks it for the modules. In one container one
/// of the two loses. So the modules stay in the application's container, the gateway gets its own and is
/// handed the modules explicitly, through the same public classes <c>AddInMemorySchema</c> uses. See
/// <c>Tests/Spikes/DDDToolkit.Spikes.FusionInProcess</c>.
/// </para>
/// </remarks>
public static class ShopSchema
{
    /// <summary>The modules, each a source schema of its own.</summary>
    private static readonly string[] Modules = ["catalog", "ordering", "inventory", "payments", "shipping"];

    /// <summary>The gateway's container, built when the endpoint is mapped.</summary>
    private sealed class Gateway
    {
        public IServiceProvider? Services { get; set; }
    }

    /// <summary>Registers every module's source schema. <see cref="MapShopGraphQL"/> adds the gateway.</summary>
    public static IServiceCollection AddShopGraphQL(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        SourceSchema(services, "catalog")
            .AddMutationType()
            .AddCatalogGraphQL();

        SourceSchema(services, "ordering")
            .AddMutationType()
            .AddSubscriptionType()
            .AddInMemorySubscriptions()
            .AddOrderingGraphQL()
            .AddOrderingProductStub();

        SourceSchema(services, "inventory")
            .AddInventoryGraphQL();

        SourceSchema(services, "payments")
            .AddPaymentsGraphQL()
            .AddPaymentsOrderStub();

        SourceSchema(services, "shipping")
            .AddShippingGraphQL()
            .AddShippingOrderStub();

        // The gateway resolves the clients for the modules from the request's services, which are this
        // container's; this container hands that one service over from the gateway's.
        var gateway = new Gateway();
        services.AddSingleton(gateway);
        services.AddSingleton(_ => (gateway.Services ?? throw new InvalidOperationException("Call MapShopGraphQL."))
            .GetRequiredService<ISourceSchemaClientScopeFactory>());

        return services;
    }

    /// <summary>The composed schema, at <c>/graphql</c>, answered by the gateway.</summary>
    public static WebApplication MapShopGraphQL(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var modules = app.Services.GetRequiredService<IRequestExecutorProvider>();
        var moduleEvents = app.Services.GetRequiredService<IRequestExecutorEvents>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();

        var composition = new SchemaComposerOptions();
        // Relay across the modules: node(id:) is answered by whichever module owns the type in the id.
        composition.Merger.EnableGlobalObjectIdentification = true;

        var gateway = services
            .AddGraphQLGatewayServer()
            .AddConfigurationProvider(_ => new InMemoryConfigurationProvider(Modules, modules, moduleEvents, composition));

        services.AddSingleton<ISourceSchemaClientFactory>(
            new InMemorySourceSchemaClientFactory(modules, moduleEvents, JsonResultFormatter.Default));

        foreach (var module in Modules)
        {
            FusionSetupUtilities.Configure(gateway,
                setup => setup.ClientConfigurationModifiers.Add(_ => new InMemorySourceSchemaClientConfiguration(module)));
        }

        var provider = services.BuildServiceProvider();
        app.Services.GetRequiredService<Gateway>().Services = provider;

        app.UseWhen(context => context.Request.Path.StartsWithSegments("/graphql"), branch =>
        {
            branch.ApplicationServices = provider;
            branch.MapGraphQL("/graphql", "_Default");
        });

        return app;
    }

    /// <summary>What every module's source schema has in common.</summary>
    private static IRequestExecutorBuilder SourceSchema(IServiceCollection services, string name)
        => services
            .AddGraphQLServer(name)
            // A schema a gateway composes: lookups inferred as keys, node fields shareable.
            .AddSourceSchemaDefaults()
            // Relay, with node(id:) as the lookup the gateway uses to fetch a module's part of a type.
            .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
            // Ids as UUIDs, [Internal] members out, value objects shareable, domain methods unpublished...
            .AddDDDToolkitTypes()
            // ...and a broken rule or an invalid value as a GraphQL error carrying its code.
            .AddDDDToolkitErrors()
            .AddQueryType();
}
