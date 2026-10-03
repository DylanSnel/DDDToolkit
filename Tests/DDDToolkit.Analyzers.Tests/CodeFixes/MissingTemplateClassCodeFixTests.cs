using System.Collections.Immutable;
using DDDToolkit.Analyzers.CodeFixes;
using DDDToolkit.Analyzers.Tests.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.CodeFixes;

/// <summary>
/// The fix for DDD00044 and DDD00049: declare the class a template or a registration takes and nobody
/// declared. Like the other fix tests, each runs the real generators for the diagnostics, applies the fix
/// through a workspace the way an IDE does, clean-up included, and runs the generators again over every file:
/// the fix is right when the diagnostic is gone and the project compiles.
/// </summary>
public class MissingTemplateClassCodeFixTests
{
    private const string Usings =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using Sample.Tenancy;

        namespace Sample;


        """;

    private const string Unit =
        """
        [OrganizationUnit<UnitId>]
        public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
        {
            private ShopUnit(UnitId id, string name) : base(id, name) { }

            public static ShopUnit Create(UnitId id, string name) => new(id, name);
        }

        """;

    /// <summary>An application on the real package: its usings, its namespace and its four ids.</summary>
    private const string PlainIds =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Tenancy;

        namespace Sample;

        [EntityId<long>]
        public readonly partial record struct TenantId;

        [EntityId<Guid>]
        public readonly partial record struct SeatId;

        [EntityId<Guid>]
        public readonly partial record struct OrganizationUnitId;

        [EntityId<Guid>]
        public readonly partial record struct RoleId;

        """;

    [Fact]
    public async Task DDD00044_inserts_the_missing_class_with_the_hosts_prefix_and_id()
    {
        var application = Usings + Unit +
            """
            [Organization<OrganizationId>]
            public sealed partial class ShopOrganization;
            """;

        var fixedSources = await Fix("DDD00044", Sources(application));

        fixedSources["Application.cs"].Should().EndWith(
            """
            [Organization<OrganizationId>]
            public sealed partial class ShopOrganization;

            [TenantAggregate<TenantId>]
            public sealed partial class ShopTenant;

            """.Replace("\r\n", "\n"),
            "the organization is ShopOrganization, declared with [Organization], so the tenant it lacks is ShopTenant");
    }

    [Fact]
    public async Task DDD00049_inserts_the_missing_class()
    {
        // Nothing the project declares takes the organization, so only the registration misses it.
        var application = Usings +
            """
            [TenantAggregate<TenantId>]
            public sealed partial class ShopTenant;

            """ + Unit;

        var fixedSources = await Fix("DDD00049", Sources(application, TemplateRegistrationTests.Registrations));

        fixedSources["Application.cs"].Should().Contain(
            """
            [TenantAggregate<TenantId>]
            public sealed partial class ShopTenant;

            [Organization<OrganizationId>]
            public sealed partial class ShopOrganization;

            """.Replace("\r\n", "\n"),
            "the registration is reported on the first class declared with one of its templates, and the new one goes after it");
    }

    [Fact]
    public async Task The_fixed_project_compiles_with_no_diagnostics()
    {
        // A host with every class but the tenant: the organization reports DDD00044, and the registration, which
        // needs the same class, stands back behind it. The one report's fix answers both.
        var application = Usings + Unit +
            """
            [Organization<OrganizationId>]
            public sealed partial class ShopOrganization;

            public static class Startup
            {
                public static Registry Register() => new Registry().AddTenancy();
            }
            """;

        Host(Sources(application, TemplateRegistrationTests.Registrations)).RunCore().ReportedDiagnostics.Select(diagnostic => diagnostic.Id)
            .Should().Equal(["DDD00044"], "one class is missing, and it is said once, on the class whose parent takes from it");

        var fixedSources = await Fix("DDD00044", Sources(application, TemplateRegistrationTests.Registrations));

        var rerun = Host(fixedSources).RunCore();
        rerun.ReportedDiagnostics.Should().BeEmpty();
        rerun.ShouldCompile();
        rerun.ShouldHaveGenerated("TenancyRegistrations.AddTenancy.Registration.");
    }

    [Fact]
    public async Task The_Tenancy_packages_unit_is_declared_with_the_applications_unit_id()
    {
        // The real package, referenced as an application references it. Its unit's parent names the id TUnitId,
        // and the application's id is OrganizationUnitId, after the template: the fix finds it by that name.
        var application =
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Tenancy;

            namespace Shop;

            [EntityId<long>]
            public readonly partial record struct TenantId;

            [EntityId<Guid>]
            public readonly partial record struct SeatId;

            [EntityId<Guid>]
            public readonly partial record struct OrganizationUnitId;

            [EntityId<Guid>]
            public readonly partial record struct RoleId;

            [TenantAggregate<TenantId>]
            public sealed partial class ShopTenant;

            [OrganizationAggregate<TenantId>]
            public sealed partial class ShopOrganization;

            [SeatAggregate<SeatId>]
            public sealed partial class ShopSeat;

            [RoleAggregate<RoleId>]
            public sealed partial class ShopRole;
            """.Replace("\r\n", "\n");

        var fixedSources = await Fix("DDD00044", new Dictionary<string, string>(StringComparer.Ordinal) { ["Application.cs"] = application }, tenancy: true);

        fixedSources["Application.cs"].Should().Contain(
            """
            [OrganizationAggregate<TenantId>]
            public sealed partial class ShopOrganization;

            [OrganizationUnit<OrganizationUnitId>]
            public sealed partial class ShopUnit;

            """.Replace("\r\n", "\n"),
            "the organization reports the unit it lacks; the new class keeps the prefix and the template's last word");

        var rerun = Host(fixedSources, tenancy: true).RunCore();
        rerun.ReportedDiagnostics.Should().BeEmpty("the seat was missing the same unit");
        rerun.ShouldCompile();
    }

    [Fact]
    public async Task A_class_named_for_its_template_alone_gives_no_prefix()
    {
        // The real package, with classes named as an application without a prefix names them. The organization
        // is called what its template is called, so there is no prefix to keep, and the unit it lacks gets the
        // template's name in full: its last word alone, Unit, would say too little.
        var application = PlainIds +
            """

            [TenantAggregate<TenantId>]
            public sealed partial class Tenant;

            [OrganizationAggregate<TenantId>]
            public sealed partial class Organization;

            [SeatAggregate<SeatId>]
            public sealed partial class Seat;

            [RoleAggregate<RoleId>]
            public sealed partial class Role;
            """.Replace("\r\n", "\n");

        var fixedSources = await Fix("DDD00044", new Dictionary<string, string>(StringComparer.Ordinal) { ["Application.cs"] = application }, tenancy: true);

        fixedSources["Application.cs"].Should().Contain(
            """
            [OrganizationAggregate<TenantId>]
            public sealed partial class Organization;

            [OrganizationUnit<OrganizationUnitId>]
            public sealed partial class OrganizationUnit;

            """.Replace("\r\n", "\n"),
            "Organization has no prefix, so the class is named after the missing template in full");

        var rerun = Host(fixedSources, tenancy: true).RunCore();
        rerun.ReportedDiagnostics.Should().BeEmpty("the seat was missing the same unit");
        rerun.ShouldCompile();
    }

    [Fact]
    public async Task A_unit_named_for_its_template_alone_gives_no_prefix_either()
    {
        // OrganizationUnit ends in Unit, the last word of its own template, as a prefixed ShopUnit does. It is
        // the template's whole name all the same, so what stands before Unit is no prefix of the application's:
        // the organization the registration lacks is Organization, not OrganizationOrganization. Nothing the
        // project declares takes the organization, so the registration is what says it is missing.
        var application = Usings + Unit.Replace("ShopUnit", "OrganizationUnit", StringComparison.Ordinal) +
            """

            [TenantAggregate<TenantId>]
            public sealed partial class Tenant;

            public static class Startup
            {
                public static Registry Register() => new Registry().AddTenancy();
            }
            """;

        var fixedSources = await Fix("DDD00049", Sources(application, TemplateRegistrationTests.Registrations));

        fixedSources["Application.cs"].Should().Contain(
            """
                public static OrganizationUnit Create(UnitId id, string name) => new(id, name);
            }

            [Organization<OrganizationId>]
            public sealed partial class Organization;

            [TenantAggregate<TenantId>]
            public sealed partial class Tenant;

            """.Replace("\r\n", "\n"),
            "the registration is reported on the unit, the first class declared with one of its templates");

        var rerun = Host(fixedSources).RunCore();
        rerun.ReportedDiagnostics.Should().BeEmpty("the organization was the one class missing");
        rerun.ShouldCompile();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("ambiguous")]
    public async Task No_fix_is_offered_when_the_id_type_is_unknown_or_ambiguous(string kind)
    {
        var application = Usings + Unit +
            """
            [Organization<OrganizationId>]
            public sealed partial class ShopOrganization;
            """;

        var sources = Sources(application);
        if (kind == "unknown")
        {
            var withoutTenantId = sources["Ids.cs"].Replace("[EntityId<int>]\npublic readonly partial record struct TenantId;", string.Empty, StringComparison.Ordinal);
            withoutTenantId.Should().NotBe(sources["Ids.cs"]);
            sources["Ids.cs"] = withoutTenantId;
        }
        else
        {
            sources["Archive.cs"] =
                """
                using DDDToolkit.Abstractions.Attributes;

                namespace Sample.Archive;

                [EntityId<int>]
                public readonly partial record struct TenantId;
                """;
        }

        var actions = await Actions("DDD00044", sources);

        actions.Should().BeEmpty("an id guessed from several, or made up, would bind the model to the wrong one");
    }

    [Fact]
    public async Task No_fix_is_offered_when_the_name_is_taken()
    {
        var application = Usings + Unit +
            """
            [Organization<OrganizationId>]
            public sealed partial class ShopOrganization;

            public sealed class ShopTenant;
            """;

        (await Actions("DDD00044", Sources(application))).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ harness

    private static Dictionary<string, string> Sources(string application, string? registrations = null)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Package.cs"] = TemplateEntityTests.Package,
            ["Ids.cs"] = TemplateEntityTests.Ids,
            ["Application.cs"] = application,
        };

        if (registrations is not null)
        {
            sources["Registrations.cs"] = registrations;
        }

        return sources.ToDictionary(pair => pair.Key, pair => pair.Value.Replace("\r\n", "\n"), StringComparer.Ordinal);
    }

    /// <param name="sources">The files, by path.</param>
    /// <param name="tenancy">Whether the project references DDDToolkit.Supporting.Tenancy, the real package.</param>
    private static GeneratorTestHost Host(IReadOnlyDictionary<string, string> sources, bool tenancy = false)
    {
        GeneratorTestHost? host = null;
        foreach (var (path, text) in sources)
        {
            host = host is null ? GeneratorTestHost.Create(text, path) : host.WithSource(text, path);
        }

        return tenancy ? host!.WithTenancy() : host!;
    }

    /// <summary>Applies the one fix offered for the first diagnostic of <paramref name="id"/>, and returns every file after it.</summary>
    private static async Task<Dictionary<string, string>> Fix(string id, Dictionary<string, string> sources, bool tenancy = false)
    {
        var actions = await Actions(id, sources, tenancy);
        actions.Should().ContainSingle();

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        var fixedSources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in solution.Projects.Single().Documents)
        {
            fixedSources[document.Name] = (await document.GetTextAsync()).ToString().Replace("\r\n", "\n");
        }

        var rerun = Host(fixedSources, tenancy).RunCore();
        rerun.ShouldNotHaveDiagnostic(id);
        rerun.ShouldNotCrash();
        return fixedSources;
    }

    private static async Task<List<CodeAction>> Actions(string id, Dictionary<string, string> sources, bool tenancy = false)
    {
        var (document, diagnostics) = await Open(id, sources, tenancy);

        var actions = new List<CodeAction>();
        await new MissingTemplateClassCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostics[0], (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }

    /// <summary>
    /// Runs the generators over the files and opens them as a workspace project, with the diagnostics of
    /// <paramref name="id"/> anchored in Application.cs, where the generators report them at a rebuilt location
    /// that the IDE maps back to the document by path and span.
    /// </summary>
    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> Open(string id, Dictionary<string, string> sources, bool tenancy)
    {
        var host = Host(sources, tenancy);
        var outcome = host.RunCore();

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            GeneratorTestHost.DefaultAssemblyName,
            GeneratorTestHost.DefaultAssemblyName,
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: host.CreateParseOptions(),
            metadataReferences: host.References));

        Document? application = null;
        foreach (var (path, text) in sources)
        {
            var document = workspace.AddDocument(project.Id, path, SourceText.From(text));
            if (path == "Application.cs")
            {
                application = document;
            }
        }

        var tree = (await application!.GetSyntaxTreeAsync())!;
        var diagnostics = outcome.GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == id && diagnostic.Location.GetLineSpan().Path == "Application.cs")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => Diagnostic.Create(diagnostic.Descriptor, Location.Create(tree, diagnostic.Location.SourceSpan), diagnostic.Properties))
            .ToImmutableArray();

        diagnostics.Should().NotBeEmpty($"the sources should report {id}");
        return (workspace.CurrentSolution.GetDocument(application.Id)!, diagnostics);
    }
}
