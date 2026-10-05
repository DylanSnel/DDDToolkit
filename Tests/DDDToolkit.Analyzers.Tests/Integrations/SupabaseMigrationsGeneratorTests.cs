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

    /// <summary>The host: an application, the only kind of project the build step can start.</summary>
    private static GeneratorTestHost HostReferencing(string module, string? export = "Write")
    {
        var host = GeneratorTestHost.Create(Host).WithAssemblyName("Shop.Host").AsApplication().WithSupabase().WithReferencedAssembly(module, "Shop.Ordering");
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
        result.ShouldContain("SupabaseMigrationSources", "SupabaseMigrationBuild.RunIfRequested(All, Rules, Functions, Contributions)");
    }

    /// <summary>A module that offers two row access contributions, as a package would.</summary>
    private static readonly string OfferingModule = OrderingModule.Replace(
        "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]",
        """
        [assembly: DDDToolkit.Abstractions.Attributes.Module("Ordering")]
        [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Ordering.OrderAudit))]
        [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Ordering.OrderArchive))]
        """,
        StringComparison.Ordinal) + """


        public sealed class OrderAudit : DDDToolkit.EntityFramework.Postgres.IRowAccessContribution
        {
            public string Owner => "ordering";

            public DDDToolkit.EntityFramework.Postgres.RowAccessContributionResult? Contribute(DbContext context, DDDToolkit.EntityFramework.Postgres.RowAccessExport export) => null;
        }

        public sealed class OrderArchive : DDDToolkit.EntityFramework.Postgres.IRowAccessContribution
        {
            public string Owner => "ordering";

            public DDDToolkit.EntityFramework.Postgres.RowAccessContributionResult? Contribute(DbContext context, DDDToolkit.EntityFramework.Postgres.RowAccessExport export) => null;
        }
        """;

    [Fact]
    public void Only_the_contributions_the_host_uses_are_passed()
    {
        var result = GeneratorTestHost.Create("[assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Ordering.OrderAudit))]\n" + Host)
            .WithAssemblyName("Shop.Host")
            .WithSupabase()
            .WithReferencedAssembly(OfferingModule, "Shop.Ordering")
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "public static global::System.Collections.Generic.IReadOnlyList<global::DDDToolkit.EntityFramework.Postgres.IRowAccessContribution> Contributions()\n" +
            "            => new global::DDDToolkit.EntityFramework.Postgres.IRowAccessContribution[]\n" +
            "            {\n" +
            "            new global::Shop.Ordering.OrderAudit(),\n" +
            "            };",
            "the initializer hands the export the contribution the host lists, created without reflection");
        result.ShouldNotContain("SupabaseMigrationSources", "OrderArchive", "an offer alone writes nothing into the host's migrations");
    }

    [Fact]
    public void An_offered_contribution_the_host_does_not_use_is_DDD00054()
    {
        var result = GeneratorTestHost.Create("[assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Ordering.OrderAudit))]\n" + Host)
            .WithAssemblyName("Shop.Host")
            .WithSupabase()
            .WithReferencedAssembly(OfferingModule, "Shop.Ordering")
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Check")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        var reported = result.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00054").Subject;
        reported.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        reported.GetMessage().Should().Be(
            "'Shop.Ordering' offers the row access contribution 'Shop.Ordering.OrderArchive', which this application does not use. Add [assembly: UseRowAccessContribution(typeof(Shop.Ordering.OrderArchive))], or list a class of yours that derives from it, to write its SQL into your migrations.");

        HostReferencing(OfferingModule, export: null).Run(GeneratorTestHost.SupabaseGenerators())
            .ShouldNotHaveDiagnostic("DDD00054");
    }

    /// <summary>
    /// A module that offers contributions whose SQL depends on what only the host knows: one the host derives from
    /// and one it closes with a type of its own.
    /// </summary>
    private static readonly string OfferingForTheHostModule = OrderingModule.Replace(
        "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]",
        """
        [assembly: DDDToolkit.Abstractions.Attributes.Module("Ordering")]
        [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Ordering.OrderAudit))]
        [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Ordering.OrderArchive<>))]
        """,
        StringComparison.Ordinal) + """


        public class OrderAudit(string table) : DDDToolkit.EntityFramework.Postgres.IRowAccessContribution
        {
            public string Table { get; } = table;

            public string Owner => "ordering";

            public DDDToolkit.EntityFramework.Postgres.RowAccessContributionResult? Contribute(DbContext context, DDDToolkit.EntityFramework.Postgres.RowAccessExport export) => null;
        }

        public class OrderArchive<TRetention> : DDDToolkit.EntityFramework.Postgres.IRowAccessContribution
        {
            public string Owner => "ordering";

            public DDDToolkit.EntityFramework.Postgres.RowAccessContributionResult? Contribute(DbContext context, DDDToolkit.EntityFramework.Postgres.RowAccessExport export) => null;
        }
        """;

    [Fact]
    public void A_class_of_the_hosts_derived_from_an_offered_contribution_or_closing_it_uses_the_offer()
    {
        var result = GeneratorTestHost.Create(
                """
                [assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Host.HostOrderAudit))]
                [assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Ordering.OrderArchive<Shop.Host.KeepForAYear>))]

                namespace Shop.Host;

                public static class Program { }

                public sealed class HostOrderAudit() : Shop.Ordering.OrderAudit("audit_entries");

                public sealed class KeepForAYear;
                """)
            .WithAssemblyName("Shop.Host")
            .WithSupabase()
            .WithReferencedAssembly(OfferingForTheHostModule, "Shop.Ordering")
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Check")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00054");
        result.ShouldContain("SupabaseMigrationSources", "            new global::Shop.Host.HostOrderAudit(),\n");
        result.ShouldContain("SupabaseMigrationSources", "            new global::Shop.Ordering.OrderArchive<global::Shop.Host.KeepForAYear>(),\n");

        var unused = HostReferencing(OfferingForTheHostModule, export: "Check").Run(GeneratorTestHost.SupabaseGenerators());
        unused.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00054").Select(diagnostic => diagnostic.GetMessage()).Should().BeEquivalentTo(
            [
                "'Shop.Ordering' offers the row access contribution 'Shop.Ordering.OrderAudit', which this application does not use. Its constructor takes what only your application can give it, so declare a class of yours that derives from it and hands that over, public sealed class YourRowAccess() : Shop.Ordering.OrderAudit(...), and add [assembly: UseRowAccessContribution(typeof(YourRowAccess))], to write its SQL into your migrations.",
                "'Shop.Ordering' offers the row access contribution 'Shop.Ordering.OrderArchive<TRetention>', which this application does not use. Add [assembly: UseRowAccessContribution(typeof(Shop.Ordering.OrderArchive<YourRetention>))], with a type of yours for TRetention, or list a class of yours that derives from it, to write its SQL into your migrations.",
            ],
            "what is suggested compiles once the host's own types are put in: an offer the export cannot create as it is goes through a class of the host's");
    }

    /// <summary>
    /// A module that offers a contribution the way a package with members does: generic over the host's class,
    /// and made with the rules of the host's resource.
    /// </summary>
    private static readonly string OfferingWithRulesModule = OrderingModule.Replace(
        "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]",
        """
        [assembly: DDDToolkit.Abstractions.Attributes.Module("Ordering")]
        [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Ordering.LedgerRowAccess<>))]
        """,
        StringComparison.Ordinal) + """


        public sealed record LedgerRules(string Name);

        public class LedgerRowAccess<TEntry>(LedgerRules rules) : DDDToolkit.EntityFramework.Postgres.IRowAccessContribution
            where TEntry : class
        {
            public LedgerRules Rules { get; } = rules;

            public string Owner => "ordering";

            public DDDToolkit.EntityFramework.Postgres.RowAccessContributionResult? Contribute(DbContext context, DDDToolkit.EntityFramework.Postgres.RowAccessExport export) => null;
        }
        """;

    [Fact]
    public void An_offer_that_is_generic_and_made_with_the_hosts_rules_is_suggested_as_a_class_of_the_hosts_that_compiles()
    {
        var unused = HostReferencing(OfferingWithRulesModule, export: "Check").Run(GeneratorTestHost.SupabaseGenerators());

        // Not typeof(Shop.Ordering.LedgerRowAccess<TEntry>), which names a type parameter nobody declared, and
        // which the export could not create with new X() either.
        unused.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00054").Select(diagnostic => diagnostic.GetMessage()).Should().Equal(
            "'Shop.Ordering' offers the row access contribution 'Shop.Ordering.LedgerRowAccess<TEntry>', which this application does not use. Its constructor takes what only your "
            + "application can give it, so declare a class of yours that derives from it and hands that over, public sealed class YourRowAccess() : "
            + "Shop.Ordering.LedgerRowAccess<YourEntry>(...), with a type of yours for TEntry, and add [assembly: UseRowAccessContribution(typeof(YourRowAccess))], "
            + "to write its SQL into your migrations.");

        // What it says, with the host's own type and rules put in, compiles, uses the offer, and is what the export gets.
        var used = GeneratorTestHost.Create(
                """
                [assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Host.YourRowAccess))]

                namespace Shop.Host;

                public static class Program { }

                public sealed class Entry;

                public sealed class YourRowAccess() : Shop.Ordering.LedgerRowAccess<Entry>(new Shop.Ordering.LedgerRules("ledger"));
                """)
            .WithAssemblyName("Shop.Host")
            .WithSupabase()
            .WithReferencedAssembly(OfferingWithRulesModule, "Shop.Ordering")
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Check")
            .Run(GeneratorTestHost.SupabaseGenerators());

        used.ShouldCompile();
        used.ShouldNotHaveDiagnostic("DDD00054");
        used.ShouldContain("SupabaseMigrationSources", "            new global::Shop.Host.YourRowAccess(),\n");
    }

    [Fact]
    public void The_row_access_rules_of_the_modules_are_listed_with_the_sql_the_core_generator_wrote_into_them()
    {
        // A real rule: the core generator translates it when the module compiles, and the host reads the result.
        var module = OrderingModule + """


            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct OrderId;

            [DDDToolkit.Abstractions.Attributes.AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public System.Guid? PlacedBy { get; private set; }
            }

            [DDDToolkit.Abstractions.Attributes.RowAccess<Order>(DDDToolkit.Abstractions.Attributes.RowOperations.Read | DDDToolkit.Abstractions.Attributes.RowOperations.Change, To = new[] { "authenticated" })]
            public static partial class ACustomerSeesTheirOrders
            {
                public static bool Allows(Order order, DDDToolkit.Abstractions.Access.Caller caller) => order.PlacedBy == caller.UserId;
            }
            """;

        var result = HostReferencing(module).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "global::DDDToolkit.EntityFramework.Postgres.RowAccessRule.For(\"Shop.Ordering.Order\", \"A customer sees their orders\", (global::DDDToolkit.Abstractions.Attributes.RowOperations)5, \"({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})\", \"authenticated\"),");
    }

    [Fact]
    public void A_rule_that_names_its_roles_symbolically_is_listed_with_the_symbols_for_the_export_to_resolve()
    {
        var module = OrderingModule + """


            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct OrderId;

            [DDDToolkit.Abstractions.Attributes.AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public System.Guid? PlacedBy { get; private set; }
            }

            [DDDToolkit.Abstractions.Attributes.RowAccess<Order>(DDDToolkit.Abstractions.Attributes.RowOperations.Read, To = [DDDToolkit.Abstractions.Attributes.RowAccessRoles.User, DDDToolkit.Abstractions.Attributes.RowAccessRoles.SystemIn])]
            public static partial class SignedInCustomersSeeTheirOrders
            {
                public static bool Allows(Order order, DDDToolkit.Abstractions.Access.Caller caller) => order.PlacedBy == caller.UserId;
            }
            """;

        var result = HostReferencing(module).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "global::DDDToolkit.EntityFramework.Postgres.RowAccessRule.For(\"Shop.Ordering.Order\", \"Signed in customers see their orders\", (global::DDDToolkit.Abstractions.Attributes.RowOperations)1, \"({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})\", \"@user\", \"@system-in\"),");
    }

    [Fact]
    public void A_column_rule_is_listed_with_the_columns_it_holds_for_the_export_to_write_its_trigger()
    {
        var module = OrderingModule + """


            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct OrderId;

            [DDDToolkit.Abstractions.Attributes.AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public System.Guid? PlacedBy { get; private set; }

                public string Status { get; private set; } = "";

                public System.DateTimeOffset? Due { get; private set; }
            }

            [DDDToolkit.Abstractions.Attributes.RowAccess<Order>(DDDToolkit.Abstractions.Attributes.RowOperations.Change, To = [DDDToolkit.Abstractions.Attributes.RowAccessRoles.User], Columns = [nameof(Order.Status), nameof(Order.Due)])]
            public static partial class TheCustomerCancels
            {
                public static bool Allows(Order order, DDDToolkit.Abstractions.Access.Caller caller) => order.PlacedBy == caller.UserId;
            }
            """;

        var result = HostReferencing(module).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "global::DDDToolkit.EntityFramework.Postgres.RowAccessRule.ForColumns(\"Shop.Ordering.Order\", \"The customer cancels\", new string[] { \"Status\", \"Due\" }, \"{columns}({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})\", \"@user\"),");
    }

    [Fact]
    public void A_rule_for_a_token_role_is_listed_by_the_token_roles_symbolic_name()
    {
        // An attribute takes constants, so a rule names a token role as the prefix and the role: a constant of
        // the module's own, which the generator reads as its value.
        var module = OrderingModule + """


            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct OrderId;

            [DDDToolkit.Abstractions.Attributes.AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public System.Guid? PlacedBy { get; private set; }
            }

            public static class ShopRoles
            {
                public const string Analyst = DDDToolkit.Abstractions.Attributes.RowAccessRoles.TokenPrefix + "analyst";
            }

            [DDDToolkit.Abstractions.Attributes.RowAccess<Order>(DDDToolkit.Abstractions.Attributes.RowOperations.Read, To = [ShopRoles.Analyst, DDDToolkit.Abstractions.Attributes.RowAccessRoles.TokenPrefix + "examiner"])]
            public static partial class AnalystsSeeEveryPlacedOrder
            {
                public static bool Allows(Order order, DDDToolkit.Abstractions.Access.Caller caller) => order.PlacedBy != null;
            }
            """;

        var result = HostReferencing(module).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "\"@token:analyst\", \"@token:examiner\"),");
    }

    [Fact]
    public void The_access_functions_of_the_modules_are_listed_with_their_names_and_sql()
    {
        var module = OrderingModule + """


            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct OrderId;

            [DDDToolkit.Abstractions.Attributes.AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public System.Guid? PlacedBy { get; private set; }
            }

            [DDDToolkit.Abstractions.Attributes.AccessFunction<Order>("ordering.placed_by_caller")]
            public static partial class PlacedByTheCaller
            {
                public static bool Allows(Order order, DDDToolkit.Abstractions.Access.Caller caller) => order.PlacedBy == caller.UserId;
            }
            """;

        var result = HostReferencing(module).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "global::DDDToolkit.EntityFramework.Postgres.RowAccessFunction.For(\"Shop.Ordering.Order\", \"ordering.placed_by_caller\", \"({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})\"),");
    }

    [Fact]
    public void A_set_shaped_access_function_is_listed_with_its_logical_name_parameters_and_shape()
    {
        var module = OrderingModule + """


            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct OrderId;

            [DDDToolkit.Abstractions.Attributes.AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public string? Team { get; private set; }
            }

            [DDDToolkit.Abstractions.Attributes.AccessFunction<Order>("orders_of_team", Shape = DDDToolkit.Abstractions.Attributes.AccessFunctionShape.Set)]
            public static partial class OrdersOfTeam
            {
                public static bool Allows(Order order, DDDToolkit.Abstractions.Access.Caller caller, string team) => order.Team == team;
            }
            """;

        var result = HostReferencing(module).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "global::DDDToolkit.EntityFramework.Postgres.RowAccessFunction.For(\"Shop.Ordering.Order\", \"ordering/orders_of_team\", \"({col:Team} IS NOT DISTINCT FROM {arg:1})\", owner: null, parameters: \"text\", shape: (global::DDDToolkit.Abstractions.Attributes.AccessFunctionShape)1),");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    public void Without_the_export_turned_on_nothing_is_written(string? export)
    {
        var result = HostReferencing(OrderingModule, export).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.GeneratedSources.Should().BeEmpty("an application that does not turn the export on is left alone");
    }

    /// <summary>
    /// A module that would give every diagnostic the generator has where the export runs: a contribution it offers
    /// and nobody lists (DDD00054), a factory whose assembly declares no module (DDD00055), and a factory the
    /// build could not create (DDD00031).
    /// </summary>
    private static readonly string ModuleWithEverythingToSay = OfferingModule
        .Replace("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]", "", StringComparison.Ordinal) + """


        [SupabaseMigrations]
        public sealed class NeedsOptions(string connectionString) : IDesignTimeDbContextFactory<OrderingContext>
        {
            public OrderingContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OrderingContext>().Options);
        }
        """;

    [Theory]
    [InlineData("Write")]
    [InlineData("Check")]
    public void A_library_with_the_export_turned_on_writes_nothing_and_reports_nothing(string export)
    {
        // A module the property reached because it was given for the whole build: the build step cannot start a
        // library, so the generator stays out of it, and says nothing of what only the host could answer.
        var referencing = GeneratorTestHost.Create(Host)
            .WithAssemblyName("Shop.Ordering.Api")
            .WithSupabase()
            .WithReferencedAssembly(ModuleWithEverythingToSay, "Shop.Ordering")
            .WithBuildProperty("SupabaseMigrationsExport", export)
            .Run(GeneratorTestHost.SupabaseGenerators());

        referencing.ShouldCompile();
        referencing.GeneratedSources.Should().BeEmpty("the build step does not run in a library, so there is nothing to hand it");
        referencing.ReportedDiagnostics.Should().BeEmpty("a module hears nothing of an export that is the host's");

        // The module itself, where its own factories are.
        var itself = GeneratorTestHost.Create(ModuleWithEverythingToSay)
            .WithAssemblyName("Shop.Ordering")
            .WithSupabase()
            .WithBuildProperty("SupabaseMigrationsExport", export)
            .Run(GeneratorTestHost.SupabaseGenerators());

        itself.GeneratedSources.Should().BeEmpty();
        itself.ReportedDiagnostics.Should().BeEmpty();

        // And the same module referenced by the host, which hears all of it.
        HostReferencing(ModuleWithEverythingToSay, export).Run(GeneratorTestHost.SupabaseGenerators())
            .ReportedDiagnostics.Select(diagnostic => diagnostic.Id).Should().BeEquivalentTo(["DDD00054", "DDD00054", "DDD00055", "DDD00031"]);
    }

    [Theory]
    [InlineData("IsTestProject", "true")]
    [InlineData("IsTestProject", "True")]
    [InlineData("IsTestingPlatformApplication", "true")]
    public void A_test_project_with_the_export_turned_on_writes_nothing_and_reports_nothing(string property, string value)
    {
        // A test project is an application, and references the host or its modules; it is still not the host.
        var result = HostReferencing(ModuleWithEverythingToSay, export: "Check")
            .WithBuildProperty(property, value)
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.GeneratedSources.Should().BeEmpty("the build step does not run in a test project");
        result.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_project_that_says_it_is_no_test_project_is_an_application_like_any_other()
    {
        var result = HostReferencing(OrderingModule)
            .WithBuildProperty("IsTestProject", "false")
            .WithBuildProperty("IsTestingPlatformApplication", "")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldContain("SupabaseMigrationSources", "global::Shop.Ordering.OrderingContextFactory>(\"Ordering\")");
    }

    [Fact]
    public void A_windows_application_exports_as_a_console_application_does()
    {
        var result = GeneratorTestHost.Create(Host)
            .WithAssemblyName("Shop.Host")
            .AsApplication(Microsoft.CodeAnalysis.OutputKind.WindowsApplication)
            .WithSupabase()
            .WithReferencedAssembly(OrderingModule, "Shop.Ordering")
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain("SupabaseMigrationSources", "[global::System.Runtime.CompilerServices.ModuleInitializer]");
    }

    [Fact]
    public void A_factory_in_the_host_itself_is_found_and_without_a_module_the_name_is_left_to_the_context()
    {
        var result = GeneratorTestHost.Create(OrderingModule.Replace("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]", ""))
            .WithSupabase()
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain("SupabaseMigrationSources", "global::Shop.Ordering.OrderingContextFactory>(null)");
    }

    [Fact]
    public void A_factory_whose_context_declares_no_module_is_DDD00055()
    {
        var unnamed = OrderingModule.Replace("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]", "", StringComparison.Ordinal);

        // In a module the host references, which the host sees through metadata alone.
        var result = HostReferencing(unnamed).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        var reported = result.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00055").Subject;
        reported.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, "the files are still exported, under a name a rename would change");
        reported.GetMessage().Should().Be(
            "'Shop.Ordering.OrderingContext' is in an assembly that declares no [assembly: Module], so its Supabase migration files are named after the context, 'ordering'. Declare the module, and the file names stay the same when the context is renamed.");
        result.ShouldContain("SupabaseMigrationSources", "global::Shop.Ordering.OrderingContextFactory>(null)");

        // In the host itself, where the warning points at the factory.
        var inTheHost = GeneratorTestHost.Create(unnamed)
            .WithSupabase()
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Check")
            .Run(GeneratorTestHost.SupabaseGenerators());
        inTheHost.ShouldHaveDiagnostic("DDD00055", at: "OrderingContextFactory");

        // A context whose name is not the module's, as the files would carry it: lower case, letters and digits.
        var odd = HostReferencing(unnamed.Replace("OrderingContext", "Order_Book2Context", StringComparison.Ordinal)).Run(GeneratorTestHost.SupabaseGenerators());
        odd.ReportedDiagnostics.Single(diagnostic => diagnostic.Id == "DDD00055").GetMessage().Should().Contain("named after the context, 'order-book2'.");
    }

    [Fact]
    public void A_factory_whose_context_declares_a_module_reports_nothing()
    {
        HostReferencing(OrderingModule).Run(GeneratorTestHost.SupabaseGenerators()).ShouldNotHaveDiagnostic("DDD00055");

        // A factory in a project of its own that declares no module, a provider's migrations say, next to the
        // module's project that holds the context and declares it: the files are named after that module.
        const string contextProject = """
            using Microsoft.EntityFrameworkCore;

            [assembly: DDDToolkit.Abstractions.Attributes.Module("Ordering")]

            namespace Shop.Ordering;

            public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options);
            """;
        const string migrationsProject = """
            using DDDToolkit.EntityFramework.Supabase;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Design;

            namespace Shop.Ordering.Migrations;

            [SupabaseMigrations]
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<Shop.Ordering.OrderingContext>
            {
                public Shop.Ordering.OrderingContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<Shop.Ordering.OrderingContext>().Options);
            }
            """;

        var apart = GeneratorTestHost.Create(Host)
            .WithAssemblyName("Shop.Host")
            .WithSupabase()
            .WithReferencedAssembly(contextProject, "Shop.Ordering")
            .WithReferencedAssembly(migrationsProject, "Shop.Ordering.Migrations")
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        apart.ShouldCompile();
        apart.ShouldNotHaveDiagnostic("DDD00055");
        apart.ShouldContain("SupabaseMigrationSources", "global::Shop.Ordering.Migrations.OrderingContextFactory>(null)");

        // And where the export is off, nothing is said about a module without a name either.
        HostReferencing(OrderingModule.Replace("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]", "", StringComparison.Ordinal), export: null)
            .Run(GeneratorTestHost.SupabaseGenerators())
            .ShouldNotHaveDiagnostic("DDD00055");
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
            .AsApplication()
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

    [Fact]
    public void The_list_is_written_with_line_feeds_whatever_the_generators_own_source_was_checked_out_with()
    {
        var result = HostReferencing(OrderingModule).Run(GeneratorTestHost.SupabaseGenerators());

        result.Source("SupabaseMigrationSources").Should().NotContain(
            "\r",
            "a checkout with core.autocrlf gives the generator's source CRLF, and the file it writes must not differ from the one a Linux build writes");
    }
}
