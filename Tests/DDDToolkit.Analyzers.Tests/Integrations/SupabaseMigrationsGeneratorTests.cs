using DDDToolkit.Analyzers.Tests.Harness;
using FluentAssertions;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// The generator the host of a modular monolith runs: it finds the <c>[SupabaseMigrations]</c> factories
/// in the modules it references and writes the list the build's export step exports.
/// </summary>
public sealed class SupabaseMigrationsGeneratorTests
{
    private const string OrderingModule = """
        using DDDToolkit.EntityFramework.Supabase;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Design;

        [assembly: DDDToolkit.Abstractions.Attributes.Module("Ordering")]

        namespace Shop.Ordering;

        public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options);

        [SupabaseMigrations]
        public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
        {
            public OrderingContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OrderingContext>().Options);
        }

        public sealed class ReportingContext(DbContextOptions<ReportingContext> options) : DbContext(options);

        // Not marked: this context is not Supabase's, and must not be exported as if it were.
        public sealed class ReportingContextFactory : IDesignTimeDbContextFactory<ReportingContext>
        {
            public ReportingContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ReportingContext>().Options);
        }
        """;

    private const string Host = "namespace Shop.Host; public static class Program { }";

    private static GeneratorTestHost HostReferencing(string module, string? export = "Write")
    {
        var host = GeneratorTestHost.Create(Host).WithAssemblyName("Shop.Host").WithSupabase().WithReferencedAssembly(module, "Shop.Ordering");
        return export is null ? host : host.WithBuildProperty("SupabaseMigrationsExport", export);
    }

    [Fact]
    public void A_host_that_turns_the_export_on_lists_every_marked_factory_it_references()
    {
        var result = HostReferencing(OrderingModule).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "global::DDDToolkit.EntityFramework.Supabase.SupabaseMigrationSource.For<global::Shop.Ordering.OrderingContext, global::Shop.Ordering.OrderingContextFactory>(\"Ordering\")");
        result.ShouldNotContain("SupabaseMigrationSources", "ReportingContext", "an unmarked factory is not Supabase's");
    }

    [Fact]
    public void The_hook_runs_before_main_and_hands_the_list_to_the_build_step()
    {
        var result = HostReferencing(OrderingModule, export: "Check").Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldContain("SupabaseMigrationSources", "[global::System.Runtime.CompilerServices.ModuleInitializer]");
        result.ShouldContain("SupabaseMigrationSources", "SupabaseMigrationBuild.RunIfRequested(All)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    public void Without_the_export_turned_on_nothing_is_written(string? export)
    {
        var result = HostReferencing(OrderingModule, export).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.GeneratedSources.Should().BeEmpty("modules, test projects and every other project referencing the package are left alone");
    }

    [Fact]
    public void A_factory_in_the_host_itself_is_found_and_without_a_module_the_name_is_left_to_the_context()
    {
        var result = GeneratorTestHost.Create(OrderingModule.Replace("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]", ""))
            .WithSupabase()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain("SupabaseMigrationSources", "global::Shop.Ordering.OrderingContextFactory>(null)");
    }

    [Fact]
    public void A_marked_factory_the_build_cannot_create_is_an_error_naming_the_reason()
    {
        const string source = """
            using DDDToolkit.EntityFramework.Supabase;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Design;

            namespace Shop.Ordering;

            public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options);

            [SupabaseMigrations]
            public sealed class NeedsOptions(string connectionString) : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OrderingContext>().Options);
            }

            [SupabaseMigrations]
            public sealed class NotAFactory;
            """;

        var result = GeneratorTestHost.Create(source)
            .WithSupabase()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldHaveDiagnostic("DDD00031", at: "NeedsOptions").GetMessage().Should().Contain("no public parameterless constructor");
        result.ShouldHaveDiagnostic("DDD00031", at: "NotAFactory").GetMessage().Should().Contain("does not implement IDesignTimeDbContextFactory<TContext>");
        result.ShouldNotContain("SupabaseMigrationSources", "NeedsOptions");
    }

    [Fact]
    public void A_factory_in_a_module_the_host_cannot_see_is_an_error_rather_than_a_module_silently_left_out()
    {
        var hidden = OrderingModule.Replace("public sealed class OrderingContextFactory", "internal sealed class OrderingContextFactory", StringComparison.Ordinal);

        var result = HostReferencing(hidden).Run(GeneratorTestHost.SupabaseGenerators());

        var reported = result.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00031").Subject;
        reported.GetMessage().Should().Contain("Shop.Ordering.OrderingContextFactory").And.Contain("'Shop.Host' cannot see it; make it public");
        result.ShouldNotContain("SupabaseMigrationSources", "OrderingContextFactory");
    }
}
