using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

public sealed class Mutation
{
    // with the prototype interceptor: the resolver returns the union
    public ReserveStockResult ReserveStock(string sku, int quantity) => Handler.Handle(sku, quantity);

    // without DTS: the resolver maps the union to FieldResult by hand
    public FieldResult<Reservation, StockBlocked, OutOfStock, SkuInvalid> ReserveStockPlain(string sku, int quantity)
        => Handler.Handle(sku, quantity) switch
        {
            Reservation reservation => reservation,
            StockBlocked error => error,
            OutOfStock error => error,
            SkuInvalid error => error,
        };
}

public sealed class Query { public string Ping => "pong"; }

public static class Program
{
    public static async Task Main()
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddMutationConventions(applyToAllMutations: true)
            .TryAddTypeInterceptor<UnionMutationInterceptor>()
            .BuildRequestExecutorAsync();

        Console.WriteLine(executor.Schema.ToString());

        foreach (var (sku, quantity) in new[] { ("tea", 2), ("tea", 9), ("blocked", 1), ("bad", 1) })
        {
            var query = $$"""
                mutation {
                  reserveStock(input: { sku: "{{sku}}", quantity: {{quantity}} }) {
                    reservation { sku quantity }
                    errors { __typename ... on Error { message } ... on OutOfStock { available } }
                  }
                }
                """;
            var result = await executor.ExecuteAsync(query);
            Console.WriteLine($"# reserveStock({sku}, {quantity})");
            Console.WriteLine(result.ToJson());
        }
    }
}
