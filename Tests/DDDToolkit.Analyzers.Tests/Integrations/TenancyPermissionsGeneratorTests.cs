using System.Text;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// What Tenancy's own generator writes: a module declares its permissions once, as the constants of a class marked
/// <c>[Permissions]</c>, and gets <c>DeclaredPermissions</c>, its part of the catalogue under the module's name, with the
/// description each constant's summary gives; a project that composes the modules gets every module's part in
/// <c>TenancyPermissionsOfModules</c>, with the registration of them. The host registers the permissions with that, and
/// an export builds the same catalogue from <c>All</c>, so no project lists the modules' permissions by hand.
/// <para>
/// The modules are projects of their own here, seen through metadata the way the host sees them, and the package is
/// the real one.
/// </para>
/// </summary>
public class TenancyPermissionsGeneratorTests
{
    private const string Written = "TenancyPermissionsOfModules";

    private const string Declared = "DeclaredPermissions";

    /// <summary>A module with two permissions, one asked at a unit and one across the tenant.</summary>
    private const string Ordering =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.Supporting.Tenancy.Access;

        [assembly: Module("Ordering")]

        namespace Shop.Ordering;

        [Permissions]
        public static class OrderingPermissions
        {
            /// <summary>See the orders.</summary>
            [AtUnit]
            public const string View = "orders.view";

            /// <summary>Refund an order.</summary>
            [AtTenant(Implies = [View])]
            public const string Refund = "orders.refund";
        }
        """;

    /// <summary>A module with one permission, described by its level rather than a summary.</summary>
    private const string Billing =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.Supporting.Tenancy.Access;

        [assembly: Module("Billing")]

        namespace Shop.Billing;

        [Permissions]
        public static class BillingPermissions
        {
            [AtTenant(Description = "See the invoices")]
            public const string View = "invoices.view";
        }
        """;

    /// <summary>The host: it registers the permissions, and builds the catalogue an export would, naming no module.</summary>
    private const string Host =
        """
        using DDDToolkit.Supporting.Tenancy.Catalogue;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Host;

        public static class Startup
        {
            public static IServiceCollection Register(IServiceCollection services) => services.AddTenancyPermissionsOfModules();

            public static TenancyCatalogue Exported() => TenancyCatalogue.Build(TenancyPermissionsOfModules.All);
        }
        """;

    /// <summary>An application in one project: its own permissions, in two classes, one of them internal.</summary>
    private const string Kiosk =
        """
        using DDDToolkit.Access;
        using DDDToolkit.Supporting.Tenancy.Access;

        namespace Kiosk;

        [Permissions]
        internal static class SalesPermissions
        {
            /// <summary>See the sales.</summary>
            [AtUnit]
            public const string View = "sales.view";
        }

        [Permissions]
        public static class StockPermissions
        {
            /// <summary>Count the stock.</summary>
            [AtUnit]
            public const string Count = "stock.count";

            /// <summary>Order stock.</summary>
            [AtTenant]
            public const string Order = "stock.order";
        }
        """;

    /// <summary>The host of a shop of <paramref name="modules"/>, each a project of its own that the generator ran over too.</summary>
    private static GeneratorTestHost Shop(string host, params (string Assembly, string Source)[] modules)
    {
        var shop = GeneratorTestHost.Create(host, "Startup.cs").WithAssemblyName("Shop.Host").WithTenancy().WithDependencyInjection();
        foreach (var (assembly, source) in modules)
        {
            shop = shop.WithReferencedProject(assembly, project => project.WithSource(source, assembly + ".cs"), GeneratorTestHost.TenancyGenerators());
        }

        return shop;
    }

    private static GeneratorRunOutcome Run(GeneratorTestHost host) => host.RunCoreAnd(GeneratorTestHost.TenancyGenerators());

    private static IEnumerable<string> WrittenFiles(GeneratorRunOutcome result, string name = Written)
        => result.GeneratedSources.Select(source => source.HintName).Where(hint => hint.StartsWith(name, StringComparison.Ordinal));

    // ------------------------------------------------------------------ what a module gets

    [Fact]
    public void A_module_gets_its_part_of_the_catalogue_from_its_permissions_class_under_its_name()
    {
        var result = Run(GeneratorTestHost.Create(Ordering, "Ordering.cs").WithAssemblyName("Shop.Ordering").WithTenancy().WithDependencyInjection());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        WrittenFiles(result, Declared).Should().Equal([Declared + ".g.cs"]);
        result.ShouldContain(Declared, "namespace Shop.Ordering;");
        result.ShouldContain(Declared, "[global::DDDToolkit.Supporting.Tenancy.Catalogue.TenancyPermissionsAttribute]");
        result.ShouldContain(Declared, "new(\"orders.view\", \"Ordering\", \"See the orders.\", Implies: null, Retired: false, Order: 10),");
        result.ShouldContain(Declared, "new(\"orders.refund\", \"Ordering\", \"Refund an order.\", Implies: new string[] { \"orders.view\" }, Retired: false, Order: 20),");
        WrittenFiles(result).Should().BeEmpty("a module states its own permissions, and composes no other module's");

        var module = result.Emit();
        var all = (IReadOnlyList<Permission>)module.StaticProperty("Shop.Ordering." + Declared, "All")!;
        all.Select(permission => (permission.Key, permission.Module, permission.Description)).Should().Equal(
            ("orders.view", "Ordering", "See the orders."),
            ("orders.refund", "Ordering", "Refund an order."));
        TenancyCatalogue.Build(all).LiveKeys.Should().Contain(["orders.view", "orders.refund"]);
    }

    [Fact]
    public void A_description_said_on_the_level_wins_over_the_summary_and_a_retired_permission_says_so()
    {
        const string Said =
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Access;
            using DDDToolkit.Supporting.Tenancy.Access;

            [assembly: Module("Billing")]

            namespace Shop.Billing;

            [Permissions]
            public static class BillingPermissions
            {
                /// <summary>See the <see cref="View"/> of an <c>invoice</c> &amp; its lines.</summary>
                [AtTenant]
                public const string View = "invoices.view";

                /// <summary>What the summary says.</summary>
                [AtUnit(Description = "What the level says", Retired = true)]
                public const string Void = "invoices.void";
            }
            """;

        var result = Run(GeneratorTestHost.Create(Said, "Billing.cs").WithAssemblyName("Shop.Billing").WithTenancy());

        result.ShouldCompile();
        var all = (IReadOnlyList<Permission>)result.Emit().StaticProperty("Shop.Billing." + Declared, "All")!;
        all.Select(permission => (permission.Key, permission.Description, permission.Retired)).Should().Equal(
            ("invoices.view", "See the View of an invoice & its lines.", false),
            ("invoices.void", "What the level says", true));
    }

    [Fact]
    public void A_list_marked_by_hand_is_refused_where_it_is_declared()
    {
        const string ByHand =
            """
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Tenancy.Catalogue;

            [assembly: Module("Ordering")]

            namespace Shop.Ordering;

            public static class OrderingKeys
            {
                [TenancyPermissions]
                public static IReadOnlyList<Permission> Permissions { get; } = [new("orders.view", "Ordering", "See the orders")];
            }
            """;

        var result = Run(GeneratorTestHost.Create(ByHand).WithAssemblyName("Shop.Ordering").WithTenancy());

        result.ShouldNotCrash();
        var reported = result.ShouldHaveDiagnostic("DDD00095", at: "Permissions");
        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().StartWith("'OrderingKeys.Permissions' is a list of permissions marked [TenancyPermissions] by hand. Declare each permission as a constant of a static class marked [Permissions]");
        result.Count("DDD00095").Should().Be(1);
    }

    // ------------------------------------------------------------------ what a project that composes gets

    [Fact]
    public void A_host_gets_every_referenced_modules_part_and_the_registration_of_them()
    {
        var result = Run(Shop(Host, ("Shop.Ordering", Ordering), ("Shop.Billing", Billing)));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        WrittenFiles(result).Should().Equal([Written + ".g.cs"], "one class for the project, whatever the number of modules");
        result.ShouldContain(Written, "namespace Shop.Host;", "the project's own namespace, so two projects that compose never name one class");
        result.ShouldContain(Written, "internal static class TenancyPermissionsOfModules");
        result.ShouldContain(
            Written,
            """
                public static global::System.Collections.Generic.IReadOnlyList<global::DDDToolkit.Supporting.Tenancy.Catalogue.Permission> All { get; } = Join(
                    global::Shop.Billing.DeclaredPermissions.All,
                    global::Shop.Ordering.DeclaredPermissions.All);
            """,
            "each module's part once, in the order of its name");
        result.ShouldContain(
            Written,
            """
                public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddTenancyPermissionsOfModules(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    => global::DDDToolkit.Supporting.Tenancy.TenancyServiceCollectionExtensions.AddTenancyPermissions(services, All);
            """,
            "the registration adds them as a module's own call would");

    }

    [Fact]
    public void An_application_in_one_project_collects_its_own_permissions_and_registers_every_one_of_them()
    {
        var result = Run(GeneratorTestHost.Create(Kiosk, "Kiosk.cs").WithAssemblyName("Kiosk").WithTenancy().WithDependencyInjection().AsApplication());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("an application's own permissions, an internal class's too, are its own to collect");

        var kiosk = result.Emit();
        var all = (IReadOnlyList<Permission>)kiosk.StaticProperty("Kiosk." + Written, "All")!;
        all.Select(permission => (permission.Key, permission.Module)).Should().Equal(
            [("sales.view", "Kiosk"), ("stock.count", "Kiosk"), ("stock.order", "Kiosk")],
            "the classes one after the other, in the order of their names, under the project's name where it declares no module");

        var services = (IServiceCollection)kiosk.CallStatic("Kiosk." + Written, "AddTenancyPermissionsOfModules", new ServiceCollection())!;
        var contribution = services.Should().ContainSingle().Which.ImplementationInstance.Should().BeOfType<PermissionContribution>().Which;
        contribution.Permissions.Should().Equal(all, "the registration adds exactly what the export builds from");
    }

    [Theory]
    [InlineData("DDD_Module")]
    [InlineData("[assembly: Module]")]
    public void An_application_in_one_project_that_declares_its_module_still_collects_its_own_permissions(string declaredWith)
    {
        // Getting started has the one project set DDD_Module, which makes it module Kiosk: it is still the program the
        // modules are composed in, and no other project would register its permissions.
        var host = GeneratorTestHost.Create(Kiosk, "Kiosk.cs").WithAssemblyName("Kiosk").WithTenancy().WithDependencyInjection().AsApplication();
        host = declaredWith == "DDD_Module"
            ? host.WithModuleFromTheBuild("Kiosk")
            : host.WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Kiosk\")]", "Module.cs");

        var result = Run(host);

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        var kiosk = result.Emit();
        var services = (IServiceCollection)kiosk.CallStatic("Kiosk." + Written, "AddTenancyPermissionsOfModules", new ServiceCollection())!;
        services.Should().ContainSingle().Which.ImplementationInstance.Should().BeOfType<PermissionContribution>()
            .Which.Permissions.Select(permission => permission.Key).Should().Equal(["sales.view", "stock.count", "stock.order"]);
    }

    [Fact]
    public void An_application_that_declares_a_module_also_collects_the_modules_it_references()
    {
        // A host that sets DDD_Module to name its own generated code is a module as well, and composes the others all the same.
        var result = Run(Shop(Host, ("Shop.Ordering", Ordering), ("Shop.Billing", Billing)).WithModuleFromTheBuild("Host").AsApplication());

        result.ShouldCompile();
        result.ShouldContain(Written, "global::Shop.Billing.DeclaredPermissions.All,");
        result.ShouldContain(Written, "global::Shop.Ordering.DeclaredPermissions.All);");
    }

    [Fact]
    public void A_project_that_cannot_see_the_service_collection_gets_the_list_alone()
    {
        var result = Run(GeneratorTestHost.Create(Kiosk, "Kiosk.cs").WithAssemblyName("Kiosk").WithTenancy().AsApplication());

        result.ShouldCompile();
        result.ShouldContain(Written, "public static global::System.Collections.Generic.IReadOnlyList<global::DDDToolkit.Supporting.Tenancy.Catalogue.Permission> All { get; }");
        result.ShouldNotContain(Written, "AddTenancyPermissionsOfModules", "an export needs the list, and has no services to add it to");
    }

    // ------------------------------------------------------------------ where nothing is composed

    [Fact]
    public void A_project_of_a_module_above_it_gets_nothing()
    {
        // The module's infrastructure or API project: it sees the module's part through its reference, and declares the module too.
        const string Infrastructure =
            """
            using DDDToolkit.Abstractions.Attributes;

            [assembly: Module("Ordering")]

            namespace Shop.Ordering.Infrastructure;

            public static class OrderingInfrastructure;
            """;

        var result = Run(GeneratorTestHost.Create(Infrastructure).WithAssemblyName("Shop.Ordering.Infrastructure").WithTenancy().WithDependencyInjection()
            .WithReferencedProject("Shop.Ordering", project => project.WithSource(Ordering, "Ordering.cs"), GeneratorTestHost.TenancyGenerators()));

        result.ShouldCompile();
        WrittenFiles(result).Should().BeEmpty("only a project that declares no module composes the modules");
        WrittenFiles(result, Declared).Should().BeEmpty("it declares no permission of its own");
    }

    [Fact]
    public void A_project_of_a_module_its_folder_declares_gets_nothing_either()
    {
        // The same project with no [assembly: Module] of its own: a Directory.Build.props sets DDD_Module for its
        // folder, and the build declares the module, as the Tenancy sample's modules do.
        var result = Run(GeneratorTestHost.Create("namespace Shop.Ordering.Infrastructure; public static class OrderingInfrastructure;")
            .WithAssemblyName("Shop.Ordering.Infrastructure").WithModuleFromTheBuild("Ordering").WithTenancy().WithDependencyInjection()
            .WithReferencedProject("Shop.Ordering", project => project.WithSource(Ordering, "Ordering.cs"), GeneratorTestHost.TenancyGenerators()));

        result.ShouldCompile();
        WrittenFiles(result).Should().BeEmpty("a module the build declares is a module, as one [assembly: Module] declares is");
    }

    [Fact]
    public void A_project_that_sees_no_permissions_gets_an_empty_list_so_its_calls_compile_the_same()
    {
        // The host's call and the export's build name no module, so they must compile before the first module declares
        // its permissions, and after the last one is taken out: an application of Tenancy alone makes them as well.
        var result = Run(Shop(Host));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(Written, "All { get; } = global::System.Array.Empty<global::DDDToolkit.Supporting.Tenancy.Catalogue.Permission>();");
        result.ShouldContain(Written, "is empty.");
        result.ShouldNotContain(Written, "Join(", "there is nothing to join");

        var host = result.Emit();
        ((IReadOnlyList<Permission>)host.StaticProperty("Shop.Host." + Written, "All")!).Should().BeEmpty();
        var services = (IServiceCollection)host.CallStatic("Shop.Host.Startup", "Register", new ServiceCollection())!;
        services.Should().ContainSingle().Which.ImplementationInstance.Should().BeOfType<PermissionContribution>()
            .Which.Permissions.Should().BeEmpty("a contribution of no permissions adds nothing to the catalogue");
    }

    [Fact]
    public void A_project_without_Tenancy_gets_nothing_and_hears_nothing()
    {
        var result = Run(GeneratorTestHost.Create("namespace Plain; public static class Nothing;"));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        WrittenFiles(result).Should().BeEmpty();
    }

    [Fact]
    public void A_list_the_host_cannot_read_is_left_out_so_the_modules_error_is_the_only_one()
    {
        // A module as a build without Tenancy's generator left it, which would have refused its lists marked by hand: the
        // host meets the lists such a build gets wrong. One is no static member, one is internal, one is read only through
        // a type parameter, one is in an extension block (where the Roslyn the tests run on parses C# 14), and one is right.
        const string Careless =
            """
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Tenancy.Catalogue;

            [assembly: Module("Ordering")]

            namespace Shop.Ordering;

            public sealed class InstanceKeys
            {
                [TenancyPermissions]
                public IReadOnlyList<Permission> Permissions { get; } = [new("orders.copy", "Ordering", "Copy an order")];
            }

            public static class HiddenKeys
            {
                [TenancyPermissions]
                internal static IReadOnlyList<Permission> Permissions { get; } = [new("orders.hide", "Ordering", "Hide an order")];
            }

            public interface IVirtualKeys
            {
                [TenancyPermissions]
                static virtual IReadOnlyList<Permission> Permissions => [new("orders.bend", "Ordering", "Bend an order")];
            }

            public static class OrderingKeys
            {
                [TenancyPermissions]
                public static IReadOnlyList<Permission> Permissions { get; } = [new("orders.view", "Ordering", "See the orders")];
            }
            """;

        const string Extended =
            """

            public static class ExtendedKeys
            {
                extension(string)
                {
                    [TenancyPermissions]
                    public static IReadOnlyList<Permission> Permissions => [new("orders.extend", "Ordering", "Extend an order")];
                }
            }
            """;

        var module = GeneratorTestHost.ParsesExtensionBlocks ? Careless + Extended : Careless;
        var result = Run(GeneratorTestHost.Create(Host, "Startup.cs").WithAssemblyName("Shop.Host").WithTenancy().WithDependencyInjection()
            .WithReferencedProject("Shop.Ordering", project => project.WithSource(module, "Ordering.cs")));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("what is wrong with the module's lists is the module's to hear, where they are declared");
        result.ShouldContain(Written, "global::Shop.Ordering.OrderingKeys.Permissions);");
        result.ShouldNotContain(Written, "InstanceKeys", "a list that is no static member cannot be read as Type.Member");
        result.ShouldNotContain(Written, "HiddenKeys", "nor can one the host does not see");
        result.ShouldNotContain(Written, "VirtualKeys", "nor one of an interface that only a type parameter reads");
        result.ShouldNotContain(Written, "ExtendedKeys", "nor one in an extension block, which code cannot name");
    }

    // ------------------------------------------------------------------ incremental

    [Fact]
    public void An_edit_of_the_host_that_changes_no_permission_writes_nothing_anew()
    {
        var first = Run(Shop(Host, ("Shop.Ordering", Ordering), ("Shop.Billing", Billing)));
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) =>
        {
            var tree = compilation.SyntaxTrees.First();
            return compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(SourceText.From(tree.GetText() + "\n// A comment that concerns nobody.\n", Encoding.UTF8), parseOptions, tree.FilePath));
        });

        var reasons = second.OutputStepReasons();
        reasons.Should().NotBeEmpty("the driver must be tracking steps, or this means nothing");
        reasons.Should().OnlyContain(
            step => step.Reason == IncrementalStepRunReason.Cached || step.Reason == IncrementalStepRunReason.Unchanged,
            "the host's lists are read off the compilation on every edit, and compare equal while they are the same");
    }

    [Fact]
    public void Permissions_the_host_declares_itself_are_written_anew()
    {
        // The control for the test above: a change that matters shows, or that one would pass on a pipeline that writes nothing.
        var first = Run(Shop(Host, ("Shop.Ordering", Ordering)));
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) => compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            SourceText.From(
                """
                using DDDToolkit.Access;
                using DDDToolkit.Supporting.Tenancy.Access;

                namespace Shop.Host;

                [Permissions]
                public static class HostPermissions
                {
                    /// <summary>Close the shop.</summary>
                    [AtTenant]
                    public const string Close = "shop.close";
                }
                """,
                Encoding.UTF8),
            parseOptions,
            "HostPermissions.cs")));

        second.ShouldCompile();
        second.OutputStepReasons().Should().Contain(step => step.Reason == IncrementalStepRunReason.Modified || step.Reason == IncrementalStepRunReason.New);
        second.ShouldContain(Written, "global::Shop.Host.DeclaredPermissions.All,");
        second.ShouldContain(Declared, "new(\"shop.close\", \"Shop.Host\", \"Close the shop.\"");
    }
}
