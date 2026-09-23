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
    : ShopScenarios<Projects.DDDToolkit_Examples_Supabase_AppHost>(shop), IClassFixture<SupabaseMonolith>;

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

    public override bool ServesGraphQL => false;
}

/// <summary>
/// The same shop as three services over pgmq, behind a gateway: every scenario the monoliths pass, with
/// the messages between services riding queues in the one Postgres they share.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Microservices.Pgmq")]
public sealed class MicroservicesOverPgmq(PgmqServices shop)
    : ShopScenarios<Projects.DDDToolkit_Examples_Pgmq_AppHost>(shop), IClassFixture<PgmqServices>;
