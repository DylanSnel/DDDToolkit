using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00071: a design-time factory whose context does not keep its migration history where the running application
/// does. <c>UseDDDToolkit</c> keeps it in the context's default schema; <c>dotnet ef</c> and the Supabase export make
/// the context through the factory, which says the same with <c>UseDDDToolkitDesignTime()</c>. A factory that says
/// nothing records the migrations where the application does not look, so the build says it at
/// <c>CreateDbContext</c>, and is silent where the factory calls the toolkit, names the history table, or has its
/// options made by a member of its own class that does either.
/// </summary>
public class DesignTimeFactoryDiagnosticTests
{
    /// <summary>
    /// A module's context, and a provider of the shop's own written the way Npgsql's is: an options extension, its
    /// relational options, which declare <c>MigrationsHistoryTable</c>, and the call that adds them.
    /// </summary>
    internal const string Ordering =
        """
        using System;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Design;
        using Microsoft.EntityFrameworkCore.Infrastructure;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Ordering;

        public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options);

        public sealed class ShopDatabaseExtension : RelationalOptionsExtension
        {
            public ShopDatabaseExtension() { }
            private ShopDatabaseExtension(ShopDatabaseExtension copyFrom) : base(copyFrom) { }
            public override DbContextOptionsExtensionInfo Info => null!;
            protected override RelationalOptionsExtension Clone() => new ShopDatabaseExtension(this);
            public override void ApplyServices(IServiceCollection services) { }
        }

        public sealed class ShopDatabaseOptions(DbContextOptionsBuilder options) : RelationalDbContextOptionsBuilder<ShopDatabaseOptions, ShopDatabaseExtension>(options);

        public static class ShopDatabase
        {
            public static DbContextOptionsBuilder<TContext> UseShopDatabase<TContext>(this DbContextOptionsBuilder<TContext> options, string connectionString, Action<ShopDatabaseOptions>? database = null)
                where TContext : DbContext
            {
                database?.Invoke(new ShopDatabaseOptions(options));
                return options;
            }

            public static DbContextOptionsBuilder UseShopDatabase(this DbContextOptionsBuilder options, string connectionString) => options;
        }


        """;

    /// <summary>
    /// Runs the analyzer over the module and <paramref name="code"/>; with <paramref name="toolkit"/> the project
    /// references DDDToolkit.EntityFramework and imports its namespace, without it Entity Framework alone.
    /// </summary>
    private static GeneratorRunOutcome Run(string code, bool toolkit = true)
    {
        var host = GeneratorTestHost.Create(Ordering + code).WithAnalyzers(GeneratorTestHost.EntityFrameworkAnalyzers());
        return (toolkit
                ? host.WithEntityFrameworkRuntime().WithSource("global using DDDToolkit.EntityFramework;", "Usings.cs")
                : host.WithEntityFramework())
            .RunCore();
    }

    [Fact]
    public void A_factory_whose_options_leave_the_toolkit_out_is_reported_at_CreateDbContext()
    {
        var result = Run(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00071");
        var reported = result.ShouldHaveDiagnostic("DDD00071", at: "CreateDbContext");
        reported.Severity.Should().Be(DiagnosticSeverity.Warning);
        reported.GetMessage().Should()
            .StartWith("'OrderingContextFactory' makes its 'OrderingContext' without UseDDDToolkitDesignTime()")
            .And.Contain("record the migrations in the provider's default schema, while a host that wires the context with UseDDDToolkit reads them in the context's own")
            .And.EndWith("Add .UseDDDToolkitDesignTime() to its options, after the provider.");
        reported.Descriptor.HelpLinkUri.Should().EndWith("diagnostics#ddd00071");
    }

    [Fact]
    public void A_factory_that_calls_UseDDDToolkitDesignTime_is_not_reported_in_either_form()
    {
        var result = Run(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").UseDDDToolkitDesignTime().Options);
            }

            public sealed class SecondContext(DbContextOptions options) : DbContext(options);

            public sealed class SecondContextFactory : IDesignTimeDbContextFactory<SecondContext>
            {
                public SecondContext CreateDbContext(string[] args)
                {
                    var options = new DbContextOptionsBuilder();
                    options.UseShopDatabase("Host=unused");
                    global::DDDToolkit.EntityFramework.DependencyInjection.UseDDDToolkitDesignTime(options);
                    return new SecondContext(options.Options);
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00071");
    }

    [Fact]
    public void A_factory_that_names_the_history_table_itself_is_not_reported()
    {
        // The table the factory names is the one the host names too: where the history is, is said in both.
        var result = Run(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>()
                        .UseShopDatabase("Host=unused", database => database.MigrationsHistoryTable("__EFMigrationsHistory", "ordering"))
                        .Options);
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00071");
    }

    [Fact]
    public void Options_made_by_a_member_of_the_factorys_own_class_are_followed_into_it()
    {
        var result = Run(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args) => new(Options(args));

                private static DbContextOptions<OrderingContext> Options(string[] args)
                    => Wired(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused")).Options;

                private static DbContextOptionsBuilder<OrderingContext> Wired(DbContextOptionsBuilder<OrderingContext> options)
                    => options.UseDDDToolkitDesignTime();
            }

            public sealed class SecondContext(DbContextOptions<SecondContext> options) : DbContext(options);

            public sealed class SecondContextFactory : IDesignTimeDbContextFactory<SecondContext>
            {
                private static DbContextOptions<SecondContext> Shared { get; } = new DbContextOptionsBuilder<SecondContext>().UseShopDatabase("Host=unused").UseDDDToolkitDesignTime().Options;

                public SecondContext CreateDbContext(string[] args) => new(Shared);
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00071");
    }

    [Fact]
    public void A_member_of_the_class_that_CreateDbContext_does_not_reach_does_not_count()
    {
        var result = Run(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options);

                public static DbContextOptions<OrderingContext> Unused()
                    => new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").UseDDDToolkitDesignTime().Options;
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00071");
    }

    [Fact]
    public void An_explicit_implementation_is_reported_and_an_abstract_factory_is_not()
    {
        var result = Run(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                OrderingContext IDesignTimeDbContextFactory<OrderingContext>.CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options);
            }

            public abstract class ContextFactoryBase : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00071");
        result.ShouldHaveDiagnostic("DDD00071", at: "CreateDbContext").GetMessage().Should().StartWith("'OrderingContextFactory'");
    }

    [Fact]
    public void A_project_without_the_toolkits_Entity_Framework_package_hears_nothing()
    {
        // Its contexts are not wired with UseDDDToolkit, so their history is where Entity Framework keeps it, in the
        // factory's context as in the host's.
        var result = Run(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options);
            }
            """,
            toolkit: false);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00071");
    }
}
