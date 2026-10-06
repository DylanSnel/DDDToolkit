using DDDToolkit.Analyzers.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// A package that declares itself a contributor of row level security, <c>[assembly: RowAccessContribution]</c>, is
/// written into the Supabase export of every application that references it: the generator makes its class in the
/// project that runs the export, in a class it writes into <c>DDDToolkit.RowAccessContributionsOfPackages.g.cs</c>,
/// from the static members the application marks with the attributes the package's constructor names.
/// </summary>
public sealed class PackageContributionsGeneratorTests
{
    /// <summary>A made-up package: a contribution written from the plans the application marks, and the grace it may mark.</summary>
    private const string PlansPackage = """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.EntityFramework.Postgres;
        using Microsoft.EntityFrameworkCore;

        [assembly: RowAccessContribution(typeof(Shop.Plans.PlanRowAccess))]

        namespace Shop.Plans;

        [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
        public sealed class PlansAttribute : System.Attribute;

        [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
        public sealed class GraceAttribute : System.Attribute;

        public class PlanRowAccess(
            [FromApplication(typeof(PlansAttribute))] System.Collections.Generic.IReadOnlyList<string> plans,
            [FromApplication(typeof(GraceAttribute))] int? graceDays = null) : IRowAccessContribution
        {
            public System.Collections.Generic.IReadOnlyList<string> Plans { get; } = plans;

            public int? GraceDays { get; } = graceDays;

            public string Owner => "plans";

            public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => null;
        }
        """;

    /// <summary>A made-up package whose contribution is made once for every resource whose rules the application marks.</summary>
    private const string LedgerPackage = """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.EntityFramework.Postgres;
        using Microsoft.EntityFrameworkCore;

        [assembly: RowAccessContribution(typeof(Shop.Ledgers.LedgerRowAccess<>))]

        namespace Shop.Ledgers;

        public sealed record LedgerRules(string Name);

        [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
        public sealed class LedgerRulesAttribute<TEntry> : System.Attribute
            where TEntry : class;

        [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
        public sealed class LedgerKeysAttribute : System.Attribute;

        public class LedgerRowAccess<TEntry>(
            [FromApplication(typeof(LedgerRulesAttribute<>))] LedgerRules rules,
            [FromApplication(typeof(LedgerKeysAttribute), Every = true)] System.Collections.Generic.IEnumerable<System.Collections.Generic.IEnumerable<string>> keys)
            : IRowAccessContribution
            where TEntry : class
        {
            public LedgerRules Rules { get; } = rules;

            public string Owner => Rules.Name;

            public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => null;
        }
        """;

    /// <summary>The host: an application with the export turned on, whose own source is <paramref name="source"/>.</summary>
    private static GeneratorTestHost Host(string source, params (string Source, string Name)[] references)
    {
        var host = GeneratorTestHost.Create(source).WithAssemblyName("Shop.Host").WithSupabase();
        foreach (var (referenced, name) in references)
        {
            host = host.WithReferencedAssembly(referenced, name);
        }

        return host.AsApplication().WithBuildProperty("SupabaseMigrationsExport", "Write");
    }

    private const string ShopPlans = """
        namespace Shop.Host;

        public static class Program { }

        public static class ShopPlans
        {
            [Shop.Plans.Plans]
            public static System.Collections.Generic.IReadOnlyList<string> All { get; } = ["free", "pro"];
        }
        """;

    [Fact]
    public void A_package_that_declares_itself_a_contributor_is_written_without_a_line_of_the_applications()
    {
        var result = Host(ShopPlans, (PlansPackage, "Shop.Plans")).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            """
                internal sealed class PlanRowAccess : global::DDDToolkit.EntityFramework.Postgres.IPackageRowAccessContribution
                {
                    private readonly global::DDDToolkit.EntityFramework.Postgres.IRowAccessContribution _contribution = new global::Shop.Plans.PlanRowAccess(
                        plans: global::Shop.Host.ShopPlans.All);
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            "the package's class is made with what the application marks, by name, and the grace it marks none of is left to its default");
        result.ShouldContain("RowAccessContributionsOfPackages", "/// <item><c>plans</c>: <c>Shop.Host.ShopPlans.All</c>, marked <c>[Plans]</c>.</item>");
        result.ShouldContain("RowAccessContributionsOfPackages", "/// <item><c>graceDays</c>: nothing is marked <c>[Grace]</c>, so its default.</item>");
        result.ShouldContain("RowAccessContributionsOfPackages", "/// To write none of it: <c>[assembly: LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess))]</c>.");
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "        public global::DDDToolkit.EntityFramework.Postgres.IRowAccessContribution Contribution => _contribution;",
            "the export names the package's class above what it writes, and not the class written here, whose version is the application's");
        result.ShouldContain("RowAccessContributionsOfPackages", "/// The access files name the package's class and <c>Shop.Plans</c> above what it writes, without a version.");
        result.ShouldContain("SupabaseMigrationSources", "            new global::DDDToolkit.EntityFramework.Supabase.Generated.PlanRowAccess(),\n");
    }

    [Fact]
    public void The_packages_contributions_come_before_the_applications_own()
    {
        var result = Host(
                "[assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Host.HostAudit))]\n" + ShopPlans + """


                public sealed class HostAudit : DDDToolkit.EntityFramework.Postgres.IRowAccessContribution
                {
                    public string Owner => "audit";

                    public DDDToolkit.EntityFramework.Postgres.RowAccessContributionResult? Contribute(Microsoft.EntityFrameworkCore.DbContext context, DDDToolkit.EntityFramework.Postgres.RowAccessExport export) => null;
                }
                """,
                (PlansPackage, "Shop.Plans"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "SupabaseMigrationSources",
            "            new global::DDDToolkit.EntityFramework.Supabase.Generated.PlanRowAccess(),\n" +
            "            new global::Shop.Host.HostAudit(),\n");
    }

    [Fact]
    public void A_member_marked_in_a_project_the_host_references_is_found_there()
    {
        const string catalogue = """
            namespace Shop.Catalogue;

            public static class ShopPlans
            {
                [Shop.Plans.Plans]
                public static readonly string[] All = ["free"];

                [Shop.Plans.Grace]
                public const int GraceDays = 14;
            }
            """;

        var result = GeneratorTestHost.Create("namespace Shop.Host; public static class Program { }")
            .WithAssemblyName("Shop.Host")
            .WithSupabase()
            .WithReferencedAssembly(PlansPackage, "Shop.Plans")
            .WithReferencedAssembly(catalogue, "Shop.Catalogue")
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Check")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "new global::Shop.Plans.PlanRowAccess(\n            plans: global::Shop.Catalogue.ShopPlans.All,\n            graceDays: global::Shop.Catalogue.ShopPlans.GraceDays);",
            "a field marks as a property does, a constant among them, and a value converts to the parameter as the language converts it");
    }

    [Fact]
    public void A_contribution_whose_data_the_application_does_not_mark_is_DDD00054_and_not_written()
    {
        var result = Host("namespace Shop.Host; public static class Program { }", (PlansPackage, "Shop.Plans")).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        var reported = result.ReportedDiagnostics.Should().ContainSingle().Subject;
        reported.Id.Should().Be("DDD00054");
        reported.Severity.Should().Be(DiagnosticSeverity.Warning);
        reported.GetMessage().Should().Be(
            "'Shop.Plans' writes the row access contribution 'Shop.Plans.PlanRowAccess' into this application's migrations, made from a static property or field of type "
            + "'IReadOnlyList<string>' marked [Plans], and this application marks none: nothing of it is written. Mark it, or write none of it with "
            + "[assembly: LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess))].");
        result.ShouldNotContain("SupabaseMigrationSources", "PlanRowAccess", "a contribution without what it is written from is not handed to the export");
        result.GeneratedSources.Select(source => source.HintName).Should().NotContain(hint => hint.Contains("RowAccessContributionsOfPackages", StringComparison.Ordinal));
    }

    [Fact]
    public void A_contribution_the_application_leaves_out_is_not_written_and_asks_for_nothing()
    {
        var result = Host(
                "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess))]\nnamespace Shop.Host; public static class Program { }",
                (PlansPackage, "Shop.Plans"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("what is left out is not asked what it is made from");
        result.ShouldNotContain("SupabaseMigrationSources", "PlanRowAccess");
    }

    [Fact]
    public void A_contribution_left_out_of_one_context_answers_nothing_for_that_context_alone()
    {
        var result = Host(
                "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess), Context = typeof(Shop.Host.ArchiveContext))]\n"
                + ShopPlans + """


                public sealed class ArchiveContext(Microsoft.EntityFrameworkCore.DbContextOptions<ArchiveContext> options) : Microsoft.EntityFrameworkCore.DbContext(options);
                """,
                (PlansPackage, "Shop.Plans"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "            => context is global::Shop.Host.ArchiveContext ? null : _contribution.Contribute(context, export);",
            "the package's class is asked about every other context, and the archive's access file gets nothing of it");
        result.ShouldContain("RowAccessContributionsOfPackages", "/// Left out of the access file of <c>Shop.Host.ArchiveContext</c>.");
        result.ShouldContain("SupabaseMigrationSources", "new global::DDDToolkit.EntityFramework.Supabase.Generated.PlanRowAccess(),");
    }

    [Fact]
    public void Two_members_marked_for_what_a_contribution_takes_once_are_DDD00066()
    {
        var result = Host(
                ShopPlans + """


                public static class OtherPlans
                {
                    [Shop.Plans.Plans]
                    public static System.Collections.Generic.IReadOnlyList<string> All { get; } = ["gold"];
                }
                """,
                (PlansPackage, "Shop.Plans"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        var reported = result.ReportedDiagnostics.Should().ContainSingle().Subject;
        reported.Id.Should().Be("DDD00066");
        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().Be(
            "The row access contribution 'Shop.Plans.PlanRowAccess' of 'Shop.Plans' cannot be made: 'Shop.Host.OtherPlans.All' and 'Shop.Host.ShopPlans.All' are marked [Plans], "
            + "and it takes one for 'plans'; mark one of them.");
        result.ShouldNotContain("SupabaseMigrationSources", "PlanRowAccess", "the build does not guess which of the two the application runs with");
    }

    [Fact]
    public void A_member_marked_that_is_not_what_the_contribution_takes_is_DDD00066_where_it_is_declared()
    {
        var result = Host(
                """
                namespace Shop.Host;

                public static class Program { }

                public sealed class ShopPlans
                {
                    [Shop.Plans.Plans]
                    public static string All { get; } = "free";

                    [Shop.Plans.Grace]
                    public int GraceDays { get; } = 14;
                }
                """,
                (PlansPackage, "Shop.Plans"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldHaveDiagnostic("DDD00066", at: "All").GetMessage().Should().EndWith(
            "'Shop.Host.ShopPlans.All', marked [Plans], is a 'string', and it takes an 'IReadOnlyList<string>' for 'plans'.");
        result.ShouldHaveDiagnostic("DDD00066", at: "GraceDays").GetMessage().Should().EndWith(
            "'Shop.Host.ShopPlans.GraceDays', marked [Grace], is not static, and nothing makes an instance of its class to read it from.");
        result.ReportedDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "DDD00054", "a member the application marked is reported for what it is, and not as one it did not mark");
        result.ShouldNotContain("SupabaseMigrationSources", "PlanRowAccess");
    }

    private const string ShopLedgers = """
        namespace Shop.Host;

        public static class Program { }

        public sealed class Invoice;

        public sealed class Refund;

        public static class ShopLedgers
        {
            [Shop.Ledgers.LedgerRules<Invoice>]
            public static Shop.Ledgers.LedgerRules Invoices { get; } = new("invoices");

            [Shop.Ledgers.LedgerRules<Refund>]
            public static Shop.Ledgers.LedgerRules Refunds { get; } = new("refunds");

            [Shop.Ledgers.LedgerKeys]
            public static string[] Billing { get; } = ["ledger.read"];

            [Shop.Ledgers.LedgerKeys]
            public static System.Collections.Generic.List<string> Audit { get; } = ["ledger.audit"];
        }
        """;

    [Fact]
    public void A_generic_contribution_is_made_once_for_every_member_marked_with_its_generic_mark()
    {
        var result = Host(ShopLedgers, (LedgerPackage, "Shop.Ledgers")).Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            """
                internal sealed class LedgerRowAccessOfInvoice : global::DDDToolkit.EntityFramework.Postgres.IPackageRowAccessContribution
                {
                    private readonly global::DDDToolkit.EntityFramework.Postgres.IRowAccessContribution _contribution = new global::Shop.Ledgers.LedgerRowAccess<global::Shop.Host.Invoice>(
                        rules: global::Shop.Host.ShopLedgers.Invoices,
                        keys: new global::System.Collections.Generic.IEnumerable<string>[]
                        {
                            global::Shop.Host.ShopLedgers.Audit,
                            global::Shop.Host.ShopLedgers.Billing,
                        });
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            "closed over the type the mark is written with, with every member marked for what it takes them all of, in the order of their names");
        result.ShouldContain("RowAccessContributionsOfPackages", "internal sealed class LedgerRowAccessOfRefund");
        result.ShouldContain("RowAccessContributionsOfPackages", "rules: global::Shop.Host.ShopLedgers.Refunds,");
        result.ShouldContain("RowAccessContributionsOfPackages", "/// To write none of it: <c>[assembly: LeaveOutRowAccessContribution(typeof(Shop.Ledgers.LedgerRowAccess&lt;&gt;))]</c>.");
        result.ShouldContain(
            "SupabaseMigrationSources",
            "            new global::DDDToolkit.EntityFramework.Supabase.Generated.LedgerRowAccessOfInvoice(),\n" +
            "            new global::DDDToolkit.EntityFramework.Supabase.Generated.LedgerRowAccessOfRefund(),\n");
    }

    [Fact]
    public void One_closing_of_a_generic_contribution_is_left_out_with_that_closing()
    {
        var result = Host(
                "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Ledgers.LedgerRowAccess<Shop.Host.Refund>))]\n" + ShopLedgers,
                (LedgerPackage, "Shop.Ledgers"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain("SupabaseMigrationSources", "LedgerRowAccessOfInvoice");
        result.ShouldNotContain("SupabaseMigrationSources", "LedgerRowAccessOfRefund");
    }

    [Fact]
    public void A_generic_contribution_nothing_closes_is_DDD00054_and_two_marks_of_one_closing_are_DDD00066()
    {
        var none = Host("namespace Shop.Host; public static class Program { }", (LedgerPackage, "Shop.Ledgers")).Run(GeneratorTestHost.SupabaseGenerators());

        none.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00054").Which.GetMessage().Should().Be(
            "'Shop.Ledgers' writes the row access contribution 'Shop.Ledgers.LedgerRowAccess<TEntry>' into this application's migrations, made from a static property or field "
            + "of type 'LedgerRules' marked [LedgerRules<TEntry>], one for each 'TEntry' it is written for, and this application marks none: nothing of it is written. "
            + "Mark it, or write none of it with [assembly: LeaveOutRowAccessContribution(typeof(Shop.Ledgers.LedgerRowAccess<>))].");

        var twice = Host(
                ShopLedgers + """


                public static class MoreLedgers
                {
                    [Shop.Ledgers.LedgerRules<Invoice>]
                    public static Shop.Ledgers.LedgerRules Invoices { get; } = new("bills");
                }
                """,
                (LedgerPackage, "Shop.Ledgers"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        twice.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00066").Which.GetMessage().Should().EndWith(
            "'Shop.Host.MoreLedgers.Invoices' and 'Shop.Host.ShopLedgers.Invoices' are marked [LedgerRules<Invoice>], and it is made once for each, from one; mark one of them.");
        twice.ShouldContain("SupabaseMigrationSources", "LedgerRowAccessOfRefund", "the closing that is marked once is still made");
        twice.ShouldNotContain("SupabaseMigrationSources", "LedgerRowAccessOfInvoice");
    }

    [Fact]
    public void Listing_a_packages_contribution_again_is_DDD00067_unless_the_package_is_left_out()
    {
        const string listed = """
            [assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Host.ShopPlanRowAccess))]

            namespace Shop.Host;

            public static class Program { }

            public static class ShopPlans
            {
                [Shop.Plans.Plans]
                public static System.Collections.Generic.IReadOnlyList<string> All { get; } = ["free", "pro"];
            }

            public sealed class ShopPlanRowAccess() : Shop.Plans.PlanRowAccess(ShopPlans.All);
            """;

        var twice = Host(listed, (PlansPackage, "Shop.Plans")).Run(GeneratorTestHost.SupabaseGenerators());

        var reported = twice.ShouldHaveDiagnostic("DDD00067", at: "DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Host.ShopPlanRowAccess))");
        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().Be(
            "'Shop.Host.ShopPlanRowAccess', listed with [assembly: UseRowAccessContribution], derives from 'Shop.Plans.PlanRowAccess', which 'Shop.Plans' writes into this "
            + "application's migrations already, because this project references it: written again, it would be written twice. Take the line out, and the class with it: "
            + "what it hands the package, mark instead. To write the package's SQL with a class of your own, leave the package's out with "
            + "[assembly: LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess))].");
        twice.ShouldNotContain("SupabaseMigrationSources", "new global::Shop.Host.ShopPlanRowAccess()", "the export is handed the package's once");

        // Left out on purpose, the application's own class is what writes the package's SQL.
        var instead = Host("[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess))]\n" + listed, (PlansPackage, "Shop.Plans"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        instead.ShouldCompile();
        instead.ReportedDiagnostics.Should().BeEmpty();
        instead.ShouldContain("SupabaseMigrationSources", "            new global::Shop.Host.ShopPlanRowAccess(),\n");
        instead.ShouldNotContain("SupabaseMigrationSources", "Generated.PlanRowAccess");
    }

    [Fact]
    public void A_class_another_project_declares_derived_from_a_packages_contribution_is_DDD00067()
    {
        // What an application that once handed the package its plans through a class of its own still declares.
        const string old = """
            [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Catalogue.ShopPlanRowAccess))]

            namespace Shop.Catalogue;

            public static class ShopPlans
            {
                [Shop.Plans.Plans]
                public static System.Collections.Generic.IReadOnlyList<string> All { get; } = ["free"];
            }

            public sealed class ShopPlanRowAccess() : Shop.Plans.PlanRowAccess(ShopPlans.All);
            """;

        var result = Host("namespace Shop.Host; public static class Program { }", (PlansPackage, "Shop.Plans"), (old, "Shop.Catalogue")).Run(GeneratorTestHost.SupabaseGenerators());

        result.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00067").Which.GetMessage().Should().StartWith(
            "'Shop.Catalogue.ShopPlanRowAccess', which 'Shop.Catalogue' declares, derives from 'Shop.Plans.PlanRowAccess', which 'Shop.Plans' writes into this application's migrations already");
        result.ShouldContain("SupabaseMigrationSources", "new global::DDDToolkit.EntityFramework.Supabase.Generated.PlanRowAccess(),");
        result.ShouldNotContain("SupabaseMigrationSources", "ShopPlanRowAccess");
    }

    [Fact]
    public void A_packages_class_the_build_cannot_make_is_DDD00066_naming_the_package()
    {
        const string package = """
            using DDDToolkit.EntityFramework.Postgres;
            using Microsoft.EntityFrameworkCore;

            [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Audit.AuditRowAccess))]

            namespace Shop.Audit;

            public class AuditRowAccess(string table) : IRowAccessContribution
            {
                public string Owner => table;

                public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => null;
            }
            """;

        var result = Host("namespace Shop.Host; public static class Program { }", (package, "Shop.Audit")).Run(GeneratorTestHost.SupabaseGenerators());

        result.ReportedDiagnostics.Should().ContainSingle().Which.GetMessage().Should().Be(
            "The row access contribution 'Shop.Audit.AuditRowAccess' of 'Shop.Audit' cannot be made: its constructor takes 'table', and does not say with [FromApplication] "
            + "where that comes from, so the build has nothing to make it with. That is the package's to fix.");
    }

    // ------------------------------------------------------------------ the toolkit's own packages

    [Fact]
    public void Tenancy_on_postgres_is_written_from_the_default_catalogue_when_the_application_marks_none()
    {
        var result = GeneratorTestHost.Create("namespace Shop.Host; public static class Program { }")
            .WithAssemblyName("Shop.Host")
            .WithSupportingDomainsOnPostgres()
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "new global::DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution(\n"
            + "            modules: global::System.Array.Empty<global::System.Collections.Generic.IEnumerable<global::DDDToolkit.Supporting.Tenancy.Catalogue.Permission>>());",
            "no catalogue marked is new ApplicationCatalogue(), and no module's keys an empty list, as a host that sets neither runs with");
        result.ShouldContain("RowAccessContributionsOfPackages", "/// <item><c>application</c>: nothing is marked <c>[TenancyCatalogue]</c>, so its default.</item>");

        // Membership on Postgres has nothing to be written from until the application marks a resource's rules.
        result.ReportedDiagnostics.Select(diagnostic => diagnostic.GetMessage()).Should().ContainSingle().Which.Should().StartWith(
            "'DDDToolkit.Supporting.Membership.Postgres' writes the row access contribution 'DDDToolkit.Supporting.Membership.Postgres.MembershipRowAccessContribution<TMember>'");
    }

    [Fact]
    public void The_toolkits_packages_are_written_from_the_catalogue_the_operators_and_the_rules_the_application_marks()
    {
        const string marks = """
            using DDDToolkit.Supporting.Membership.Access;
            using DDDToolkit.Supporting.Tenancy;
            using DDDToolkit.Supporting.Tenancy.Catalogue;

            namespace Shop.Host;

            public static class Program { }

            public sealed class DocumentShare;

            public static class ShopCatalogue
            {
                [TenancyCatalogue]
                public static ApplicationCatalogue Application { get; } = new();

                [TenancyOperators]
                public static System.Collections.Generic.IReadOnlyList<string> Operators { get; } = ["operator"];

                [TenancyPermissions]
                public static System.Collections.Generic.IReadOnlyList<Permission> Permissions { get; } = [];

                [MembershipRules<DocumentShare>]
                public static MembershipRules Documents => null!;
            }
            """;

        var result = GeneratorTestHost.Create(marks)
            .WithAssemblyName("Shop.Host")
            .WithSupportingDomainsOnPostgres()
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "new global::DDDToolkit.Supporting.Membership.Postgres.MembershipRowAccessContribution<global::Shop.Host.DocumentShare>(\n            rules: global::Shop.Host.ShopCatalogue.Documents);");
        result.ShouldContain("RowAccessContributionsOfPackages", "application: global::Shop.Host.ShopCatalogue.Application,");
        result.ShouldContain("RowAccessContributionsOfPackages", "operatorTokenRoles: global::Shop.Host.ShopCatalogue.Operators);");
        result.ShouldContain("RowAccessContributionsOfPackages", "global::Shop.Host.ShopCatalogue.Permissions,");
        result.ShouldContain(
            "SupabaseMigrationSources",
            "            new global::DDDToolkit.EntityFramework.Supabase.Generated.MembershipRowAccessContributionOfDocumentShare(),\n" +
            "            new global::DDDToolkit.EntityFramework.Supabase.Generated.TenancyRowAccessContribution(),\n");
    }

    /// <summary>What a host with Tenancy on Postgres declares around its catalogue, with Membership's SQL left out.</summary>
    private const string CatalogueHost = """
        using DDDToolkit.Supporting.Tenancy.Catalogue;

        [assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(DDDToolkit.Supporting.Membership.Postgres.MembershipRowAccessContribution<>))]

        namespace Shop.Host;

        public static class Program { }

        public static class ShopCatalogue
        {
            [TenancyCatalogue]
            public static ApplicationCatalogue Application { get; } = new();
        }
        """;

    private static GeneratorRunOutcome WithTenancyOnPostgres(string source)
        => GeneratorTestHost.Create(source)
            .WithAssemblyName("Shop.Host")
            .WithSupportingDomainsOnPostgres()
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run(GeneratorTestHost.SupabaseGenerators());

    [Fact]
    public void A_tenancy_catalogue_marked_twice_is_DDD00066()
    {
        var result = WithTenancyOnPostgres(CatalogueHost + """


            public static class OtherCatalogue
            {
                [TenancyCatalogue]
                public static ApplicationCatalogue Application { get; } = new();
            }
            """);

        var reported = result.ReportedDiagnostics.Should().ContainSingle().Subject;
        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().Be(
            "The row access contribution 'DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution' of 'DDDToolkit.Supporting.Tenancy.Postgres' cannot be made: "
            + "'Shop.Host.OtherCatalogue.Application' and 'Shop.Host.ShopCatalogue.Application' are marked [TenancyCatalogue], and it takes one for 'application'; mark one of them.");
        result.ShouldNotContain("SupabaseMigrationSources", "TenancyRowAccessContribution", "policies written from a catalogue the build guessed could grant what the host does not");
    }

    [Fact]
    public void A_tenancy_catalogue_marked_that_is_built_already_is_DDD00066_where_it_is_declared()
    {
        var result = WithTenancyOnPostgres(CatalogueHost.Replace(
            "public static ApplicationCatalogue Application { get; } = new();",
            "public static TenancyCatalogue Application { get; } = TenancyCatalogue.Build([]);",
            StringComparison.Ordinal));

        result.ShouldHaveDiagnostic("DDD00066", at: "Application").GetMessage().Should().EndWith(
            "'Shop.Host.ShopCatalogue.Application', marked [TenancyCatalogue], is a 'TenancyCatalogue', and it takes an 'ApplicationCatalogue' for 'application'.");
        result.ReportedDiagnostics.Should().ContainSingle("a catalogue marked is never passed over for the default");
        result.ShouldNotContain("SupabaseMigrationSources", "TenancyRowAccessContribution");
    }

    [Fact]
    public void A_package_with_a_mark_of_its_own_still_has_the_keys_it_marks_for_tenancy_in_the_catalogue()
    {
        // A supporting domain of a third party: a contribution made from a mark of its own, and keys of its own for
        // Tenancy's catalogue, which Tenancy's generator collects into the host as every module's.
        const string subscriptions = """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.EntityFramework.Postgres;
            using DDDToolkit.Supporting.Tenancy.Catalogue;
            using Microsoft.EntityFrameworkCore;

            [assembly: RowAccessContribution(typeof(Acme.Subscriptions.SubscriptionRowAccess))]

            namespace Acme.Subscriptions;

            [ApplicationMark]
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public sealed class PlansAttribute : System.Attribute;

            public static class SubscriptionKeys
            {
                [TenancyPermissions]
                public static System.Collections.Generic.IReadOnlyList<Permission> Permissions { get; } = [new("subscriptions.view", "Subscriptions", "Sees the subscriptions")];

                // An example of the package's own, which is no application's mark.
                [Plans]
                public static string[] Example { get; } = ["example"];
            }

            public class SubscriptionRowAccess([FromApplication(typeof(PlansAttribute))] string[]? plans = null) : IRowAccessContribution
            {
                public string Owner => "subscriptions";

                public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => null;
            }
            """;

        var result = GeneratorTestHost.Create(
                "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(DDDToolkit.Supporting.Membership.Postgres.MembershipRowAccessContribution<>))]\n"
                + "namespace Shop.Host; public static class Program { }")
            .WithAssemblyName("Shop.Host")
            .WithSupportingDomainsOnPostgres()
            .WithReferencedAssembly(subscriptions, "Acme.Subscriptions")
            .AsApplication()
            .WithBuildProperty("SupabaseMigrationsExport", "Write")
            .Run([.. GeneratorTestHost.SupabaseGenerators(), .. GeneratorTestHost.TenancyGenerators()]);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("TenancyPermissionsOfModules", "global::Acme.Subscriptions.SubscriptionKeys.Permissions", "the host registers the package's keys as a module's");
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "new global::DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution(\n"
            + "            modules: new global::System.Collections.Generic.IEnumerable<global::DDDToolkit.Supporting.Tenancy.Catalogue.Permission>[]\n"
            + "            {\n"
            + "                global::Acme.Subscriptions.SubscriptionKeys.Permissions,\n"
            + "            });",
            "the export writes Tenancy's policies from the same keys, or the host's start-up check would find them written from another catalogue");
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "new global::Acme.Subscriptions.SubscriptionRowAccess();",
            "what the package marks with its own attribute is no mark of the application's");
    }

    // ------------------------------------------------------------------ a module's own

    /// <summary>A module, Projects, with a trigger of its own it offers as a package would declare one.</summary>
    private const string ProjectsModule = """
        using DDDToolkit.EntityFramework.Postgres;
        using Microsoft.EntityFrameworkCore;

        [assembly: DDDToolkit.Abstractions.Attributes.Module("Projects")]
        [assembly: DDDToolkit.Abstractions.Attributes.RowAccessContribution(typeof(Shop.Projects.UnitTrigger))]

        namespace Shop.Projects;

        public sealed class UnitTrigger : IRowAccessContribution
        {
            public string Owner => "projects";

            public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => null;
        }
        """;

    [Fact]
    public void A_modules_own_contribution_is_offered_and_written_where_the_host_lists_it()
    {
        var forgotten = Host("namespace Shop.Host; public static class Program { }", (ProjectsModule, "Shop.Projects")).Run(GeneratorTestHost.SupabaseGenerators());

        forgotten.ShouldCompile();
        var reported = forgotten.ReportedDiagnostics.Should().ContainSingle().Subject;
        reported.Id.Should().Be("DDD00069");
        reported.Severity.Should().Be(DiagnosticSeverity.Warning);
        reported.GetMessage().Should().Be(
            "'Shop.Projects', a module, offers the row access contribution 'Shop.Projects.UnitTrigger', which this project does not list, so its SQL is not written into "
            + "the migrations. Add [assembly: UseRowAccessContribution(typeof(Shop.Projects.UnitTrigger))], or list a class of yours that derives from it.");
        forgotten.ShouldNotContain("SupabaseMigrationSources", "UnitTrigger", "a module's SQL is the application's, and is written where the host says so");
        forgotten.GeneratedSources.Select(source => source.HintName).Should().NotContain(
            hint => hint.Contains("RowAccessContributionsOfPackages", StringComparison.Ordinal),
            "a module is no package: nothing is made for it");

        var listed = Host(
                "[assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Projects.UnitTrigger))]\nnamespace Shop.Host; public static class Program { }",
                (ProjectsModule, "Shop.Projects"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        listed.ShouldCompile();
        listed.ReportedDiagnostics.Should().BeEmpty();
        listed.ShouldContain("SupabaseMigrationSources", "            new global::Shop.Projects.UnitTrigger(),\n");
    }

    // ------------------------------------------------------------------ what a line to leave out names

    [Fact]
    public void A_context_to_leave_out_of_that_is_no_context_is_DDD00068_and_leaves_nothing_out()
    {
        const string contexts = """


            public sealed class ArchiveContext<TRow>(Microsoft.EntityFrameworkCore.DbContextOptions options) : Microsoft.EntityFrameworkCore.DbContext(options);
            """;

        var result = Host(
                "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess), Context = typeof(string))]\n"
                + "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess), Context = typeof(Shop.Host.ArchiveContext<>))]\n"
                + ShopPlans + contexts,
                (PlansPackage, "Shop.Plans"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Select(diagnostic => (diagnostic.Id, diagnostic.Severity, Message: diagnostic.GetMessage())).Should().BeEquivalentTo(
            [
                ("DDD00068", DiagnosticSeverity.Warning,
                    "[assembly: LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess), Context = typeof(string))] leaves nothing out: 'string' is no context: Context "
                    + "names a class derived from DbContext, whose access file it is left out of"),
                ("DDD00068", DiagnosticSeverity.Warning,
                    "[assembly: LeaveOutRowAccessContribution(typeof(Shop.Plans.PlanRowAccess), Context = typeof(Shop.Host.ArchiveContext<>))] leaves nothing out: "
                    + "'Shop.Host.ArchiveContext<>' is left open, and Context names one context, closed with the type arguments it is declared with"),
            ]);
        result.ShouldContain(
            "RowAccessContributionsOfPackages",
            "            => _contribution.Contribute(context, export);",
            "a line that names no context leaves the contribution in every access file, and the code written for it compiles");
    }

    [Fact]
    public void A_line_that_names_nothing_a_package_writes_is_DDD00068()
    {
        var result = Host(
                "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Ledgers.LedgerRowAccess<Shop.Host.Payout>))]\n"
                + "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Host.Payout))]\n"
                + "[assembly: DDDToolkit.Abstractions.Attributes.LeaveOutRowAccessContribution(typeof(Shop.Projects.UnitTrigger))]\n"
                + "[assembly: DDDToolkit.Abstractions.Attributes.UseRowAccessContribution(typeof(Shop.Projects.UnitTrigger))]\n"
                + ShopLedgers + "\n\npublic sealed class Payout;\n",
                (LedgerPackage, "Shop.Ledgers"),
                (ProjectsModule, "Shop.Projects"))
            .Run(GeneratorTestHost.SupabaseGenerators());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().OnlyContain(diagnostic => diagnostic.Id == "DDD00068");
        result.ReportedDiagnostics.Select(diagnostic => diagnostic.GetMessage()).Should().BeEquivalentTo(
            [
                "[assembly: LeaveOutRowAccessContribution(typeof(Shop.Ledgers.LedgerRowAccess<Shop.Host.Payout>))] leaves nothing out: nothing the application marks makes "
                + "'Shop.Ledgers.LedgerRowAccess<Shop.Host.Payout>'",
                "[assembly: LeaveOutRowAccessContribution(typeof(Shop.Host.Payout))] leaves nothing out: no package this project references declares 'Shop.Host.Payout' a "
                + "contribution it writes",
                "[assembly: LeaveOutRowAccessContribution(typeof(Shop.Projects.UnitTrigger))] leaves nothing out: 'Shop.Projects.UnitTrigger' is a module's own contribution, "
                + "which only a line of [assembly: UseRowAccessContribution] writes; take that line out instead",
            ]);
        result.ShouldContain("SupabaseMigrationSources", "LedgerRowAccessOfInvoice(),");
        result.ShouldContain("SupabaseMigrationSources", "new global::Shop.Projects.UnitTrigger(),");
    }
}
