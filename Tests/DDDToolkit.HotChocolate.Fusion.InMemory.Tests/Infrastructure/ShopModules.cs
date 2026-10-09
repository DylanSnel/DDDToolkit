using System.Collections.Concurrent;
using System.Globalization;
using DDDToolkit.Exceptions;
using GreenDonut;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

// Two modules of one application, each with a source schema of its own and the toolkit's conventions, the way
// a host hands every module the same ones: Catalog owns a product's name, Inventory its stock. Both say what
// their resolvers saw while they ran, so a test can ask the gateway what reached them.

/// <summary>Registers the two modules and what they share.</summary>
internal static class ShopModules
{
    public const string Catalog = "catalog";

    public const string Inventory = "inventory";

    /// <summary>The header the pipeline reads the caller from.</summary>
    public const string CallerHeader = "X-Caller";

    /// <summary>The header the pipeline answers with the id of the HTTP request's own scope.</summary>
    public const string RequestScopeHeader = "X-Request-Scope";

    /// <summary>The conventions a host gives every module's schema, so the schemas cannot disagree on a shared type.</summary>
    public static IRequestExecutorBuilder AddHostConventions(this IRequestExecutorBuilder graphql, EnumValueSpelling spelling = EnumValueSpelling.LowerSnakeCase)
        => graphql
            .AddDDDToolkitTypes()
            .AddDDDToolkitErrors()
            .AddDDDToolkitMutationConventions()
            .AddDDDToolkitEnumValues(spelling)
            .ModifyOptions(options =>
            {
                options.DefaultQueryDependencyInjectionScope = DependencyInjectionScope.Resolver;
                options.DefaultMutationDependencyInjectionScope = DependencyInjectionScope.Request;
            });

    /// <summary>Both modules with the same conventions, and the services their resolvers ask for.</summary>
    public static void AddShop(this WebApplicationBuilder builder, int meetingOf = 3)
    {
        builder.Services.AddShopServices(meetingOf);
        builder.Services.AddCatalog();
        builder.Services.AddInventory();
    }

    public static IServiceCollection AddShopServices(this IServiceCollection services, int meetingOf = 3)
        => services
            .AddScoped<ScopeMarker>()
            .AddSingleton(new Meeting(meetingOf))
            .AddSingleton<StepLog>()
            .AddSingleton<BatchLog>()
            .AddSingleton<SourceCalls>();

    public static IRequestExecutorBuilder AddCatalog(this IServiceCollection services, EnumValueSpelling spelling = EnumValueSpelling.LowerSnakeCase)
        => services
            .AddGraphQLServer(Catalog)
            .AddSourceSchemaDefaults()
            .AddHostConventions(spelling)
            .AddQueryType()
            .AddMutationType()
            .AddTypeExtension<CatalogQueries>()
            .AddTypeExtension<CatalogMutations>()
            .AddDiagnosticEventListener(schema => new CountsCalls(Catalog, schema.GetRootServiceProvider().GetRequiredService<SourceCalls>()));

    public static IRequestExecutorBuilder AddInventory(this IServiceCollection services, EnumValueSpelling spelling = EnumValueSpelling.LowerSnakeCase)
        => services
            .AddGraphQLServer(Inventory)
            .AddSourceSchemaDefaults()
            .AddHostConventions(spelling)
            .AddQueryType()
            .AddMutationType()
            .AddTypeExtension<InventoryQueries>()
            .AddTypeExtension<InventoryMutations>()
            .AddDataLoader<StockLoader>()
            .AddDiagnosticEventListener(schema => new CountsCalls(Inventory, schema.GetRootServiceProvider().GetRequiredService<SourceCalls>()));

    /// <summary>
    /// What a host's own middleware does before the gateway: it makes the caller and the reader's language
    /// ambient for the rest of the request, and says which scope the HTTP request itself got.
    /// </summary>
    public static void UseAmbientCaller(this WebApplication app)
        => app.Use(async (context, next) =>
        {
            Ambient.Caller.Value = context.Request.Headers[CallerHeader].FirstOrDefault();

            if (context.Request.Headers.AcceptLanguage.FirstOrDefault() is { Length: > 0 } language)
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            }

            context.Response.Headers[RequestScopeHeader] = context.RequestServices.GetRequiredService<ScopeMarker>().Id.ToString();
            await next(context);
        });
}

/// <summary>The caller of the request, ambient: set where the request comes in, read wherever it is needed.</summary>
internal static class Ambient
{
    public static readonly AsyncLocal<string?> Caller = new();
}

/// <summary>A scoped service: two resolvers that got the same one ran in the same scope.</summary>
public sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>What a piece of a module's code saw while it ran: the ambient caller, the reader's language and its scope.</summary>
[Shareable]
public sealed record Witness(string? Caller, string Culture, Guid Scope)
{
    public static Witness Now(Guid scope = default) => new(Ambient.Caller.Value, CultureInfo.CurrentUICulture.Name, scope);
}

/// <summary>
/// Where resolvers that run side by side meet: each waits until as many have arrived as were expected, or gives
/// up after a while. Resolvers that run one after the other never meet.
/// </summary>
public sealed class Meeting(int expected)
{
    private readonly TaskCompletionSource _everybodyIsHere = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public async Task<bool> ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrived) >= expected)
        {
            _everybodyIsHere.TrySetResult();
        }

        var met = await Task.WhenAny(_everybodyIsHere.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)) == _everybodyIsHere.Task;
        return met;
    }
}

/// <summary>Whether a resolver met the others, and the scope it ran in.</summary>
public sealed record Attendance(bool Together, Guid Scope);

/// <summary>What a mutation field did, and when: the order is counted over the whole application.</summary>
public sealed record Step(string Name, Guid Scope, int Started, int Ended);

/// <summary>Counts the moments mutation fields start and end, so a test sees whether two overlapped.</summary>
public sealed class StepLog
{
    private int _moment;

    public int Next() => Interlocked.Increment(ref _moment);
}

/// <summary>Every batch a data loader was asked for, with its keys.</summary>
public sealed class BatchLog
{
    public ConcurrentQueue<int[]> Batches { get; } = new();
}

/// <summary>How many requests each source schema executed.</summary>
public sealed class SourceCalls
{
    private readonly ConcurrentDictionary<string, int> _calls = new();

    public int Of(string schema) => _calls.GetValueOrDefault(schema);

    public void Count(string schema) => _calls.AddOrUpdate(schema, 1, static (_, calls) => calls + 1);
}

/// <summary>Counts a source schema's requests: one for every call the gateway makes to it.</summary>
internal sealed class CountsCalls(string schema, SourceCalls calls) : ExecutionDiagnosticEventListener
{
    public override IDisposable ExecuteRequest(RequestContext context)
    {
        calls.Count(schema);
        return base.ExecuteRequest(context);
    }
}

// ---------------------------------------------------------------- Catalog

/// <summary>A product as Catalog knows it: its name.</summary>
[GraphQLName("Product")]
[EntityKey("id")]
public sealed record CatalogProduct(int Id, string Name);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class CatalogQueries
{
    /// <summary>Catalog's products. Inventory keeps stock of the first three, and has never heard of the fourth.</summary>
    private static readonly Dictionary<int, string> Names = new() { [1] = "Coffee", [2] = "Tea", [3] = "Cocoa", [4] = "Chai" };

    /// <summary>The lookup a product is fetched by, by clients and by the gateway.</summary>
    [Lookup]
    public CatalogProduct? GetProductById(int id) => Names.TryGetValue(id, out var name) ? new CatalogProduct(id, name) : null;

    /// <summary>The products on the shelf, so one answer names several.</summary>
    public CatalogProduct[] GetProducts() => [.. Names.Where(product => product.Key <= 3).Select(product => new CatalogProduct(product.Key, product.Value))];

    /// <summary>What a plain resolver sees.</summary>
    public Witness GetResolverSaw([Service] ScopeMarker scope) => Witness.Now(scope.Id);

    /// <summary>A field that waits for the fields beside it.</summary>
    public async Task<Attendance> GetMeetingAsync([Service] Meeting meeting, [Service] ScopeMarker scope, CancellationToken cancellationToken)
        => new(await meeting.ArriveAsync(cancellationToken), scope.Id);

    /// <summary>A query the caller may not make.</summary>
    public string GetPriceList()
        => throw new RefusalException(
            "catalog.price-list-closed",
            RefusalKind.NotPermitted,
            "The price list is not open to this caller.",
            new Dictionary<string, object?> { ["List"] = "wholesale", ["Tier"] = 2 });
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class CatalogMutations
{
    public const string NameRequired = "catalog.name-required";

    /// <summary>Renames a product, or refuses a blank name.</summary>
    public CatalogProduct ProductRename(int id, string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new RefusalException(
                NameRequired,
                RefusalKind.Invalid,
                "A product needs a name.",
                new Dictionary<string, object?> { [RefusalException.FieldArgument] = "name", ["MaxLength"] = 40 })
            : new CatalogProduct(id, name);

    /// <summary>A command that takes a moment, and says when it ran and in which scope.</summary>
    public async Task<Step> StepRecordAsync(string name, [Service] StepLog log, [Service] ScopeMarker scope, CancellationToken cancellationToken)
    {
        var started = log.Next();
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        return new Step(name, scope.Id, started, log.Next());
    }
}

// ---------------------------------------------------------------- Inventory

/// <summary>A product as Inventory knows it: its stock, and what the lookup and the data loader behind it saw.</summary>
[GraphQLName("Product")]
[EntityKey("id")]
public sealed record StockedProduct(int Id, int OnHand, Witness LoaderSaw)
{
    public Witness? LookupSaw { get; init; }
}

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class InventoryQueries
{
    /// <summary>The lookup the gateway fetches Inventory's part of a product by. Clients do not see it.</summary>
    [Lookup]
    [Internal]
    public async Task<StockedProduct?> GetProductByIdAsync(int id, StockLoader stock, [Service] ScopeMarker scope, CancellationToken cancellationToken)
        => await stock.LoadAsync(id, cancellationToken) is { } product ? product with { LookupSaw = Witness.Now(scope.Id) } : null;

    /// <summary>A query field of Inventory's own: a source schema needs one.</summary>
    public int GetStockTotal() => StockLoader.OnHand.Values.Sum();
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class InventoryMutations
{
    /// <summary>Adds to a product's stock, or refuses a quantity that adds nothing.</summary>
    public StockedProduct ProductRestock(int id, int quantity)
        => quantity <= 0
            ? throw new RefusalException(
                "inventory.quantity-invalid",
                RefusalKind.Invalid,
                "A restock adds at least one.",
                new Dictionary<string, object?> { [RefusalException.FieldArgument] = "quantity", ["Quantity"] = quantity })
            : new StockedProduct(id, StockLoader.OnHand.GetValueOrDefault(id) + quantity, Witness.Now());
}

/// <summary>Reads stock by product id, as many as one batch asks for. Inventory knows products 1 to 3.</summary>
public sealed class StockLoader(BatchLog log, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<int, StockedProduct>(batchScheduler, options)
{
    public static readonly IReadOnlyDictionary<int, int> OnHand = new Dictionary<int, int> { [1] = 12, [2] = 7, [3] = 0 };

    protected override Task<IReadOnlyDictionary<int, StockedProduct>> LoadBatchAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken)
    {
        log.Batches.Enqueue([.. keys]);
        var saw = Witness.Now();

        return Task.FromResult<IReadOnlyDictionary<int, StockedProduct>>(
            keys.Where(OnHand.ContainsKey).ToDictionary(key => key, key => new StockedProduct(key, OnHand[key], saw)));
    }
}
