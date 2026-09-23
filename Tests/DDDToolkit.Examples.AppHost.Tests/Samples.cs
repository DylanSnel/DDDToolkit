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
