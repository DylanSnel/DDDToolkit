namespace DDDToolkit.Examples.AppHost.Tests;

// One class per sample: which AppHost, which resource serves the shop, and the trait CI filters on.
// The scenarios themselves are ShopScenarios, the same for every sample.

public sealed class SupabaseMonolith : ShopFixture<Projects.DDDToolkit_Examples_Supabase_AppHost>
{
    protected override string ShopResource => "monolith";
}

/// <summary>
/// The modular monolith on Postgres, schema and all applied from <c>supabase/migrations</c> the way
/// Supabase applies them.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "ModularMonolith.Supabase")]
public sealed class ModularMonolithOnSupabase(SupabaseMonolith shop)
    : SupabaseShopScenarios<Projects.DDDToolkit_Examples_Supabase_AppHost>(shop), IClassFixture<SupabaseMonolith>;

public sealed class SupabaseMonolithOverQueues : ShopFixture<Projects.DDDToolkit_Examples_Supabase_AppHost>
{
    protected override string ShopResource => "monolith";

    protected override string[] Arguments => ["--Messaging=pgmq"];
}

/// <summary>
/// The same monolith with its modules talking through Supabase Queues, pgmq 1.5.1 as Supabase ships it,
/// instead of in process: nothing reaches a module except through the queue.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "ModularMonolith.Supabase.Pgmq")]
public sealed class ModularMonolithOverSupabaseQueues(SupabaseMonolithOverQueues shop)
    : SupabaseShopScenarios<Projects.DDDToolkit_Examples_Supabase_AppHost>(shop), IClassFixture<SupabaseMonolithOverQueues>;

public sealed class SqlServerMonolith : ShopFixture<Projects.DDDToolkit_Examples_SqlServer_AppHost>
{
    protected override string ShopResource => "monolith";
}

/// <summary>The same monolith on SQL Server, migrating its own schemas on start-up.</summary>
[Trait("Category", "Samples")]
[Trait("Sample", "ModularMonolith.SqlServer")]
public sealed class ModularMonolithOnSqlServer(SqlServerMonolith shop)
    : ShopScenarios<Projects.DDDToolkit_Examples_SqlServer_AppHost>(shop), IClassFixture<SqlServerMonolith>;

public sealed class PgmqServices : ShopFixture<Projects.DDDToolkit_Examples_Pgmq_AppHost>
{
    protected override string ShopResource => "gateway";

    protected override IEnumerable<string> ResourcesToWaitFor => ["storefront", "payments", "fulfilment", "gateway"];
}

/// <summary>
/// The same shop as three services over pgmq, behind a gateway: every scenario the monoliths pass, with
/// the messages between services riding queues in the one Postgres they share.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Microservices.Pgmq")]
public sealed class MicroservicesOverPgmq(PgmqServices shop)
    : ShopScenarios<Projects.DDDToolkit_Examples_Pgmq_AppHost>(shop), IClassFixture<PgmqServices>;

public sealed class WolverineServices : ShopFixture<Projects.DDDToolkit_Examples_Wolverine_AppHost>
{
    protected override string ShopResource => "gateway";

    protected override IEnumerable<string> ResourcesToWaitFor => ["storefront", "payments", "fulfilment", "gateway"];
}

/// <summary>
/// The same shop as three services over RabbitMQ with Wolverine, each on a database of its own, Storefront
/// on SQL Server and the others on Postgres.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Microservices.Wolverine")]
public sealed class MicroservicesOverWolverine(WolverineServices shop)
    : ShopScenarios<Projects.DDDToolkit_Examples_Wolverine_AppHost>(shop), IClassFixture<WolverineServices>;

public sealed class MassTransitServices : ShopFixture<Projects.DDDToolkit_Examples_MassTransit_AppHost>
{
    protected override string ShopResource => "gateway";

    protected override IEnumerable<string> ResourcesToWaitFor => ["storefront", "payments", "fulfilment", "gateway"];
}

/// <summary>
/// The same shop as three services over RabbitMQ with MassTransit 8, each on a SQL Server database of its own.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Microservices.MassTransit")]
public sealed class MicroservicesOverMassTransit(MassTransitServices shop)
    : ShopScenarios<Projects.DDDToolkit_Examples_MassTransit_AppHost>(shop), IClassFixture<MassTransitServices>;
