using HotChocolate;
using HotChocolate.Execution;
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

    default:
        Console.WriteLine("Cases: compose, endpoint.");
        return 2;
}

// ChilliCream's TwoSchemas types, as in InMemoryConnectorTests at tag 16.6.6. There both root classes are
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
    public ReviewsProduct? GetProductById(int id) => id is >= 1 and <= 3 ? new ReviewsProduct(id) : null;
}

[EntityKey("id")]
[GraphQLName("Product")]
public record ReviewsProduct(int Id);
