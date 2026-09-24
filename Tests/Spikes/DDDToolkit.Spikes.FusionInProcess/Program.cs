using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Connectors.InMemory;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Transport.Formatters;
using HotChocolate.Types.Composite;

// Fusion in-process, the way a modular monolith would use it: every module serves a source schema of its
// own, and HotChocolate.Fusion.Connectors.InMemory composes them in this process and calls them directly,
// without HTTP. Two cases, each small enough to paste into an issue.
//
//   dotnet run -- compose    ChilliCream's own TwoSchemas test (InMemoryConnectorTests at tag 16.6.6),
//                            verbatim: does BuildGatewayAsync() finish?
//   dotnet run -- endpoint                 two source schemas and a gateway served over HTTP with
//                                          MapGraphQL(): does the endpoint reach the gateway?
//   dotnet run -- endpoint-gateway-first   the same, with the gateway registered before the schemas.
//   dotnet run -- endpoint-own-container   the gateway in a service container of its own, handed the
//                                          source schemas of the application's container, and served
//                                          at /graphql from there. Public API only.

var mode = args.FirstOrDefault() ?? "compose";
var wait = TimeSpan.FromSeconds(20);

switch (mode)
{
    case "compose":
    {
        var services = new ServiceCollection();
        services.AddGraphQL("products").AddQueryType<ProductsQuery>(query => query.Name("Query")).AddSourceSchemaDefaults();
        services.AddGraphQL("reviews").AddQueryType<ReviewsQuery>(query => query.Name("Query")).AddSourceSchemaDefaults();
        services.AddGraphQLGateway().AddInMemorySchema("products").AddInMemorySchema("reviews");

        var building = services.BuildGatewayAsync().AsTask();
        if (await Task.WhenAny(building, Task.Delay(wait)) != building)
        {
            Console.WriteLine($"compose: BuildGatewayAsync() has not finished after {wait.TotalSeconds}s.");
            return 1;
        }

        var result = await building.Result.ExecuteAsync("{ productById(id: 1) { id name } }");
        Console.WriteLine("compose: " + result.ToJson());
        return 0;
    }

    case "endpoint":
    case "endpoint-gateway-first":
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://localhost:5397");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // The order matters: HotChocolate and Fusion both register the one IRequestExecutorProvider the
        // endpoint asks, with TryAdd, so whichever comes first answers for every schema name.
        if (mode == "endpoint-gateway-first")
        {
            builder.Services.AddGraphQLGatewayServer().AddInMemorySchema("products").AddInMemorySchema("reviews");
        }

        builder.Services.AddGraphQLServer("products").AddQueryType<ProductsQuery>(query => query.Name("Query")).AddSourceSchemaDefaults();
        builder.Services.AddGraphQLServer("reviews").AddQueryType<ReviewsQuery>(query => query.Name("Query")).AddSourceSchemaDefaults();

        if (mode == "endpoint")
        {
            builder.Services.AddGraphQLGatewayServer().AddInMemorySchema("products").AddInMemorySchema("reviews");
        }

        var app = builder.Build();
        app.MapGraphQL();
        var starting = app.StartAsync();
        if (await Task.WhenAny(starting, Task.Delay(wait)) != starting)
        {
            Console.WriteLine($"{mode}: the application has not started after {wait.TotalSeconds}s.");
            return 1;
        }

        await starting;

        using var http = new HttpClient { Timeout = wait };
        var response = await http.PostAsync("http://localhost:5397/graphql",
            new StringContent("""{"query":"{ productById(id: 1) { id name } }"}""", System.Text.Encoding.UTF8, "application/json"));
        Console.WriteLine($"{mode}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        await app.StopAsync();
        return response.IsSuccessStatusCode ? 0 : 1;
    }

    case "endpoint-own-container":
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://localhost:5397");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // The application's container holds the source schemas, and HotChocolate is its only executor
        // provider, so nothing competes for IRequestExecutorProvider here.
        builder.Services.AddGraphQLServer("products").AddQueryType<ProductsQuery>(query => query.Name("Query")).AddSourceSchemaDefaults();
        builder.Services.AddGraphQLServer("reviews").AddQueryType<ReviewsQuery>(query => query.Name("Query")).AddSourceSchemaDefaults();

        // The gateway resolves the clients for the source schemas from the request's services, which are
        // the application's; the application hands that one service over from the gateway's container.
        IServiceProvider? gatewayProvider = null;
        builder.Services.AddSingleton(_ => gatewayProvider!.GetRequiredService<ISourceSchemaClientScopeFactory>());

        var app = builder.Build();
        var sourceSchemas = app.Services.GetRequiredService<IRequestExecutorProvider>();
        var sourceSchemaEvents = app.Services.GetRequiredService<IRequestExecutorEvents>();
        string[] names = ["products", "reviews"];

        // The gateway's container: Fusion is its executor provider, and it is told explicitly where the
        // source schemas are, through the same public classes AddInMemorySchema uses.
        var gatewayServices = new ServiceCollection();
        gatewayServices.AddLogging();
        gatewayServices.AddHttpClient();
        var gateway = gatewayServices.AddGraphQLGatewayServer()
            .AddConfigurationProvider(_ => new InMemoryConfigurationProvider(names, sourceSchemas, sourceSchemaEvents))
            .ModifyRequestOptions(options => options.IncludeExceptionDetails = true);
        gatewayServices.AddSingleton<ISourceSchemaClientFactory>(
            new InMemorySourceSchemaClientFactory(sourceSchemas, sourceSchemaEvents, JsonResultFormatter.Default));
        foreach (var name in names)
        {
            FusionSetupUtilities.Configure(gateway,
                setup => setup.ClientConfigurationModifiers.Add(_ => new InMemorySourceSchemaClientConfiguration(name)));
        }

        gatewayProvider = gatewayServices.BuildServiceProvider();

        // /graphql answered from the gateway's container.
        app.UseWhen(context => context.Request.Path.StartsWithSegments("/graphql"), branch =>
        {
            branch.ApplicationServices = gatewayProvider;
            branch.MapGraphQL("/graphql", "_Default");
        });

        var starting = app.StartAsync();
        if (await Task.WhenAny(starting, Task.Delay(wait)) != starting)
        {
            Console.WriteLine($"{mode}: the application has not started after {wait.TotalSeconds}s.");
            return 1;
        }

        await starting;

        using var http = new HttpClient { Timeout = wait };
        var response = await http.PostAsync("http://localhost:5397/graphql",
            new StringContent("""{"query":"{ productById(id: 1) { id name reviewCount } }"}""", System.Text.Encoding.UTF8, "application/json"));
        Console.WriteLine($"{mode}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        await app.StopAsync();
        return response.IsSuccessStatusCode ? 0 : 1;
    }

    default:
        Console.WriteLine("Cases: compose, endpoint, endpoint-gateway-first, endpoint-own-container.");
        return 2;
}

// ChilliCream's TwoSchemas types, as in InMemoryConnectorTests at tag 16.6.6, except that the reviews'
// Product has a field of its own, so a query can prove that the gateway merges the two. There both root classes are
// called Query; here they have names of their own, so the root type is named Query when it is added:
// composition requires the root query type of every source schema to be called Query.

public class ProductsQuery
{
    [Lookup]
    public ProductsProduct? GetProductById(int id) => id is >= 1 and <= 3 ? new ProductsProduct(id, $"Product {id}") : null;
}

[EntityKey("id")]
[GraphQLName("Product")]
public record ProductsProduct(int Id, string Name);

public class ReviewsQuery
{
    [Lookup]
    [Internal]
    public ReviewsProduct? GetProductById(int id) => id is >= 1 and <= 3 ? new ReviewsProduct(id, ReviewCount: id * 7) : null;
}

[EntityKey("id")]
[GraphQLName("Product")]
public record ReviewsProduct(int Id, int ReviewCount);
