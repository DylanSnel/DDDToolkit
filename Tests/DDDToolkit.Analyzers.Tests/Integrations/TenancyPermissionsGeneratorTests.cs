using System.Text;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// What Tenancy's own generator writes: a module states its keys once, on the list it marks with
/// <c>[TenancyPermissions]</c>, and a project that composes the modules gets every module's list in
/// <c>TenancyPermissionsOfModules</c>, with the registration of them. The host registers the keys with that, and an
/// export builds the same catalogue from <c>All</c>, so no project lists the modules' keys by hand.
/// <para>
/// The modules are projects of their own here, seen through metadata the way the host sees them, and the package is
/// the real one.
/// </para>
/// </summary>
public class TenancyPermissionsGeneratorTests
{
    private const string Written = "TenancyPermissionsOfModules";

    /// <summary>A module that keeps its keys in a property, one of them marked as managing access.</summary>
    private const string Ordering =
        """
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Tenancy.Catalogue;

        [assembly: Module("Ordering")]

        namespace Shop.Ordering;

        public static class OrderingKeys
        {
            [TenancyPermissions]
            public static IReadOnlyList<Permission> Permissions { get; } =
            [
                new("orders.view", "Ordering", "See the orders"),
                new("orders.refund", "Ordering", "Refund an order", ManagesAccess: true),
            ];
        }
        """;

    /// <summary>A module that keeps its keys in a field, as an array.</summary>
    private const string Billing =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Tenancy.Catalogue;

        [assembly: Module("Billing")]

        namespace Shop.Billing;

        public static class BillingKeys
        {
            [TenancyPermissions]
            public static readonly Permission[] Keys = [new("invoices.view", "Billing", "See the invoices")];
        }
        """;

    /// <summary>The host: it registers the keys, and builds the catalogue an export would, naming no module.</summary>
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

    /// <summary>
    /// An application in one project, which declares no module: its own lists, one of them internal. Compiled as the
    /// program it is (<see cref="GeneratorTestHost.AsApplication"/>), it may keep that one internal; compiled as a
    /// library it may not, since a project that references it would not see the list.
    /// </summary>
    private const string Kiosk =
        """
        using System.Collections.Generic;
        using DDDToolkit.Supporting.Tenancy.Catalogue;

        namespace Kiosk;

        internal static class SalesKeys
        {
            [TenancyPermissions]
            internal static IEnumerable<Permission> Permissions => [new("sales.view", "Sales", "See the sales")];
        }

        public static class StockKeys
        {
            [TenancyPermissions]
            public static IReadOnlyList<Permission> Permissions { get; } =
            [
                new("stock.count", "Stock", "Count the stock"),
                new("stock.order", "Stock", "Order stock"),
            ];
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

    private static IEnumerable<string> WrittenFiles(GeneratorRunOutcome result)
        => result.GeneratedSources.Select(source => source.HintName).Where(name => name.StartsWith(Written, StringComparison.Ordinal));

    // ------------------------------------------------------------------ what a project that composes gets

    [Fact]
    public void A_host_gets_every_referenced_modules_list_and_the_registration_of_them()
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
                    global::Shop.Billing.BillingKeys.Keys,
                    global::Shop.Ordering.OrderingKeys.Permissions);
            """,
            "each list once, in the order of its name, a field as well as a property");
        result.ShouldContain(
            Written,
            """
                public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddTenancyPermissionsOfModules(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    => global::DDDToolkit.Supporting.Tenancy.TenancyServiceCollectionExtensions.AddTenancyPermissions(services, All);
            """,
            "the registration adds them as a module's own call would");
    }

    [Fact]
    public void An_application_in_one_project_collects_its_own_lists_and_registers_every_key_of_them()
    {
        var result = Run(GeneratorTestHost.Create(Kiosk, "Kiosk.cs").WithAssemblyName("Kiosk").WithTenancy().WithDependencyInjection().AsApplication());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("an application reads its own internal list itself, and no project composes from it");

        var kiosk = result.Emit();
        var all = (IReadOnlyList<Permission>)kiosk.StaticProperty("Kiosk." + Written, "All")!;
        all.Select(permission => permission.Key).Should().Equal(["sales.view", "stock.count", "stock.order"], "the lists one after the other, in the order of their names");

        var services = (IServiceCollection)kiosk.CallStatic("Kiosk." + Written, "AddTenancyPermissionsOfModules", new ServiceCollection())!;
        var contribution = services.Should().ContainSingle().Which.ImplementationInstance.Should().BeOfType<PermissionContribution>().Which;
        contribution.Permissions.Should().Equal(all, "the registration adds exactly what the export builds from");
        TenancyCatalogue.Build(contribution.Permissions).LiveKeys.Should().Contain(["sales.view", "stock.count", "stock.order"]);
    }

    [Fact]
    public void A_project_that_cannot_see_the_service_collection_gets_the_list_alone()
    {
        var result = Run(GeneratorTestHost.Create(Kiosk, "Kiosk.cs").WithAssemblyName("Kiosk").WithTenancy().AsApplication());

        result.ShouldCompile();
        result.ShouldContain(Written, "public static global::System.Collections.Generic.IReadOnlyList<global::DDDToolkit.Supporting.Tenancy.Catalogue.Permission> All { get; }");
        result.ShouldNotContain(Written, "AddTenancyPermissionsOfModules", "an export needs the list, and has no services to add it to");
    }

    // ------------------------------------------------------------------ where nothing is written

    [Fact]
    public void A_modules_own_project_gets_nothing_and_hears_nothing()
    {
        var result = Run(GeneratorTestHost.Create(Ordering, "Ordering.cs").WithAssemblyName("Shop.Ordering").WithTenancy().WithDependencyInjection());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        WrittenFiles(result).Should().BeEmpty("a module states its own keys, and composes no other module's");
    }

    [Fact]
    public void A_project_of_a_module_above_it_gets_nothing_either()
    {
        // The module's infrastructure or API project: it sees the module's list through its reference, and declares the module too.
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
    }

    [Fact]
    public void A_project_of_a_module_its_folder_declares_gets_nothing_either()
    {
        // The same project with no [assembly: Module] of its own: a Directory.Build.props sets DDD_Module and
        // DDD_DeclareModule for its folder, and the build declares the module, as the Tenancy sample's modules do.
        var result = Run(GeneratorTestHost.Create("namespace Shop.Ordering.Infrastructure; public static class OrderingInfrastructure;")
            .WithAssemblyName("Shop.Ordering.Infrastructure").WithModuleFromTheBuild("Ordering").WithTenancy().WithDependencyInjection()
            .WithReferencedProject("Shop.Ordering", project => project.WithSource(Ordering, "Ordering.cs"), GeneratorTestHost.TenancyGenerators()));

        result.ShouldCompile();
        WrittenFiles(result).Should().BeEmpty("a module the build declares is a module, as one [assembly: Module] declares is");
    }

    [Fact]
    public void A_project_that_sees_no_marked_list_gets_an_empty_list_so_its_calls_compile_the_same()
    {
        // The host's call and the export's build name no module, so they must compile before the first module marks
        // its list, and after the last one is taken out: an application of Tenancy alone makes them as well.
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
            .Which.Permissions.Should().BeEmpty("a contribution of no keys adds nothing to the catalogue");
    }

    [Fact]
    public void A_project_without_Tenancy_gets_nothing_and_hears_nothing()
    {
        var result = Run(GeneratorTestHost.Create("namespace Plain; public static class Nothing;"));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        WrittenFiles(result).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ a list that cannot be read

    [Theory]
    [InlineData("public class Keys { [TenancyPermissions] public IReadOnlyList<Permission> Permissions { get; } = []; }", "is not static")]
    [InlineData("public static class Keys { [TenancyPermissions] public static IReadOnlyList<Permission> Permissions { set { } } }", "has no getter")]
    [InlineData("public static class Keys<T> { [TenancyPermissions] public static IReadOnlyList<Permission> Permissions { get; } = []; }", "is declared in the generic type 'Keys<T>'")]
    [InlineData("public static class Keys { [TenancyPermissions] public static IReadOnlyList<string> Permissions { get; } = []; }", "is of type 'IReadOnlyList<string>', which is no sequence of Permission")]
    [InlineData("internal static class Keys { [TenancyPermissions] public static IReadOnlyList<Permission> Permissions { get; } = []; }", "is not public, and its project is a library")]
    [InlineData("public static class Keys { [TenancyPermissions] internal static readonly Permission[] Permissions = []; }", "is not public, and its project is a library")]
    [InlineData("public static class Keys { [TenancyPermissions] public static IReadOnlyList<Permission> Permissions { internal get; set; } = []; }", "is not public, and its project is a library")]
    [InlineData("public interface IKeys { [TenancyPermissions] static virtual IReadOnlyList<Permission> Permissions => []; }", "is a static virtual or abstract member of an interface, which is read only through a type parameter")]
    [InlineData("public static class Keys { extension(string) { [TenancyPermissions] public static IReadOnlyList<Permission> Permissions => []; } }", "is declared in 'Keys.extension(string)', which code cannot name")]
    [InlineData("file static class Keys { [TenancyPermissions] public static IReadOnlyList<Permission> Permissions { get; } = []; }", "is declared in the file-local type 'Keys', which no other file can name")]
    public void A_modules_list_no_other_project_can_read_is_reported_where_it_is_declared(string declaration, string because)
    {
        var source =
            $$"""
              using System.Collections.Generic;
              using DDDToolkit.Abstractions.Attributes;
              using DDDToolkit.Supporting.Tenancy.Catalogue;

              [assembly: Module("Ordering")]

              namespace Shop.Ordering;

              {{declaration}}
              """;

        var result = Run(GeneratorTestHost.Create(source).WithAssemblyName("Shop.Ordering").WithTenancy());

        result.ShouldNotCrash();
        var reported = result.ShouldHaveDiagnostic("DDD00063", at: "Permissions");
        reported.Severity.Should().Be(DiagnosticSeverity.Error, "the module's keys would be missing from the catalogue, with nothing else to say so");
        reported.GetMessage().Should().Contain("is marked [TenancyPermissions] and " + because).And.Contain("so its keys reach no catalogue");
        result.Count("DDD00063").Should().Be(1);
    }

    [Fact]
    public void An_application_may_keep_its_own_list_internal()
    {
        var result = Run(GeneratorTestHost.Create(Kiosk).WithAssemblyName("Kiosk").WithTenancy().AsApplication());

        result.ShouldNotHaveDiagnostic("DDD00063");
        result.ShouldContain(Written, "global::Kiosk.SalesKeys.Permissions");
    }

    [Fact]
    public void A_library_that_declares_no_module_makes_its_list_public_all_the_same()
    {
        // A project the host references without [assembly: Module]: a module that leaves its Module.cs out, or an
        // application's projects before they declare modules. The host could not read an internal list there, and
        // would leave its keys out without a word, so the library hears it where the list is declared.
        var result = Run(GeneratorTestHost.Create(Kiosk).WithAssemblyName("Kiosk").WithTenancy());

        result.ShouldNotCrash();
        result.ShouldHaveDiagnostic("DDD00063", at: "Permissions").GetMessage()
            .Should().Be("'SalesKeys.Permissions' is marked [TenancyPermissions] and is not public, and its project is a library: the project that composes "
                         + "the modules reads it from outside, so its keys reach no catalogue. Make it a public static property or field with a getter, "
                         + "declared in a class that is not generic, whose type is a sequence of Permission.");
        result.Count("DDD00063").Should().Be(1, "StockKeys is public");
        result.CompilationErrors.Should().BeEmpty();
        result.ShouldNotContain(Written, "SalesKeys", "a list that is reported is left out, so the report is the one error");
        result.ShouldContain(Written, "global::Kiosk.StockKeys.Permissions);");
    }

    [Theory]
    [InlineData("public static class Keys { [TenancyPermissions] private static IReadOnlyList<Permission> Permissions => []; }", "cannot be read outside the type it is declared in")]
    [InlineData("public class Keys { [TenancyPermissions] protected static readonly Permission[] Permissions = []; }", "cannot be read outside the type it is declared in")]
    [InlineData("public static class Keys { [TenancyPermissions] public static IReadOnlyList<Permission> Permissions { private get; set; } = []; }", "cannot be read outside the type it is declared in")]
    [InlineData("public static class Outer { private static class Keys { [TenancyPermissions] internal static IReadOnlyList<Permission> Permissions => []; } }", "cannot be read outside the type it is declared in")]
    [InlineData("file static class Keys { [TenancyPermissions] internal static IReadOnlyList<Permission> Permissions => []; }", "is declared in the file-local type 'Keys', which no other file can name")]
    public void An_applications_own_list_it_cannot_read_from_outside_its_type_is_reported_and_left_out(string declaration, string because)
    {
        var source =
            $$"""
              using System.Collections.Generic;
              using DDDToolkit.Supporting.Tenancy.Catalogue;

              namespace Kiosk;

              {{declaration}}
              """;

        var result = Run(GeneratorTestHost.Create(source).WithAssemblyName("Kiosk").WithTenancy().WithDependencyInjection().AsApplication());

        result.ShouldNotCrash();
        result.ShouldHaveDiagnostic("DDD00063", at: "Permissions").GetMessage().Should().Contain("is marked [TenancyPermissions] and " + because);
        result.Count("DDD00063").Should().Be(1);
        result.CompilationErrors.Should().BeEmpty("the class written beside the list leaves it out, where it would not compile");
        result.ShouldContain(Written, "All { get; } = global::System.Array.Empty<");
    }

    [Fact]
    public void A_list_the_host_cannot_read_is_left_out_so_the_modules_error_is_the_only_one()
    {
        // The module as its own build left it: without Tenancy's generator, which would have stopped it, so the host
        // meets the lists a module gets wrong. One is no static member, one is internal, one is read only through a
        // type parameter, one is in an extension block, and one is right.
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

            public static class ExtendedKeys
            {
                extension(string)
                {
                    [TenancyPermissions]
                    public static IReadOnlyList<Permission> Permissions => [new("orders.extend", "Ordering", "Extend an order")];
                }
            }

            public static class OrderingKeys
            {
                [TenancyPermissions]
                public static IReadOnlyList<Permission> Permissions { get; } = [new("orders.view", "Ordering", "See the orders")];
            }
            """;

        var result = Run(GeneratorTestHost.Create(Host, "Startup.cs").WithAssemblyName("Shop.Host").WithTenancy().WithDependencyInjection()
            .WithReferencedProject("Shop.Ordering", project => project.WithSource(Careless, "Ordering.cs")));

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
    public void An_edit_of_the_host_that_changes_no_list_writes_nothing_anew()
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
    public void A_list_the_host_marks_itself_is_written_anew()
    {
        // The control for the test above: a change that matters shows, or that one would pass on a pipeline that writes nothing.
        var first = Run(Shop(Host, ("Shop.Ordering", Ordering)));
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) => compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            SourceText.From(
                """
                using System.Collections.Generic;
                using DDDToolkit.Supporting.Tenancy.Catalogue;

                namespace Shop.Host;

                public static class HostKeys
                {
                    [TenancyPermissions]
                    public static IReadOnlyList<Permission> Permissions { get; } = [new("shop.close", "Shop", "Close the shop")];
                }
                """,
                Encoding.UTF8),
            parseOptions,
            "HostKeys.cs")));

        second.ShouldCompile();
        second.OutputStepReasons().Should().Contain(step => step.Reason == IncrementalStepRunReason.Modified || step.Reason == IncrementalStepRunReason.New);
        second.ShouldContain(Written, "global::Shop.Host.HostKeys.Permissions,");
    }
}
