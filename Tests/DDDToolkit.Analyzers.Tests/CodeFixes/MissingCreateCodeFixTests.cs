using System.Collections.Immutable;
using DDDToolkit.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.CodeFixes;

/// <summary>
/// The fix for DDD00067: a Tenancy class over an id without a <c>Create()</c> gets one added to the id, wherever the
/// id is declared, with a body that throws until the application says how a new id is made; an id written by hand
/// gets <c>ICreatableEntityId&lt;TId&gt;</c> as well, or alone when it has a <c>Create()</c>. Like the other fix
/// tests, each runs the real generators for the diagnostic, applies the fix through a workspace the way an IDE does,
/// and runs the generators again: the fix is right when the diagnostic is gone and the project compiles.
/// </summary>
public class MissingCreateCodeFixTests
{
    private const string Classes =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Tenancy;

        namespace Shop;

        [TenantAggregate<TenantId>]
        public sealed partial class Tenant;

        [OrganizationAggregate<TenantId>]
        public sealed partial class Organization;

        [OrganizationUnit<OrganizationUnitId>]
        public sealed partial class OrganizationUnit;

        [SeatAggregate<SeatId>]
        public sealed partial class Seat;

        [RoleAggregate<RoleId>]
        public sealed partial class Role;
        """;

    private static string Ids(string tenantId)
        => $$"""
             using System;
             using DDDToolkit.Abstractions.Attributes;

             namespace Shop;

             {{tenantId}}

             [EntityId<Guid>]
             public readonly partial record struct SeatId;

             [EntityId<Guid>]
             public readonly partial record struct OrganizationUnitId;

             [EntityId<Guid>]
             public readonly partial record struct RoleId;
             """;

    [Fact]
    public async Task An_id_declared_with_a_semicolon_gets_braces_and_a_create_that_says_what_to_write()
    {
        var fixedSources = await Fix(Ids("[EntityId<long>]\npublic readonly partial record struct TenantId;"));

        fixedSources["Ids.cs"].Should().Contain(
            """
            [EntityId<long>]
            public readonly partial record struct TenantId
            {
                /// <summary>A new TenantId, made in code before the save.</summary>
                public static TenantId Create() => throw new NotImplementedException("Make a new TenantId in code: a snowflake, or the next number of a block a HiLo sequence hands out.");
            }

            [EntityId<Guid>]
            """.Replace("\r\n", "\n"));
        fixedSources["Classes.cs"].Should().Be(Classes.Replace("\r\n", "\n"), "the fix goes where the id is declared, not where the class names it");
    }

    [Fact]
    public async Task An_id_with_members_of_its_own_keeps_them_and_gets_the_create_after_them()
    {
        var fixedSources = await Fix(Ids(
            """
            [EntityId<long>]
            public readonly partial record struct TenantId
            {
                public bool IsDemo => Value < 0;
            }
            """));

        fixedSources["Ids.cs"].Should().Contain(
            """
                public bool IsDemo => Value < 0;

                /// <summary>A new TenantId, made in code before the save.</summary>
                public static TenantId Create() => throw new NotImplementedException(
            """.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task An_id_written_by_hand_gets_a_create_and_the_interface_the_generator_would_have_added()
    {
        var fixedSources = await Fix(
            Ids("public readonly partial record struct TenantId(long Value) : DDDToolkit.Abstractions.Interfaces.IEntityId<long>;"),
            "Add a Create() and ICreatableEntityId<TenantId> to 'TenantId'");

        fixedSources["Ids.cs"].Should()
            .Contain("using DDDToolkit.Abstractions.Interfaces;")
            .And.Contain("public readonly partial record struct TenantId(long Value) : DDDToolkit.Abstractions.Interfaces.IEntityId<long>, ICreatableEntityId<TenantId>")
            .And.Contain("public static TenantId Create() => throw new NotImplementedException(\"Make a new TenantId in code: a snowflake, ");
    }

    [Fact]
    public async Task An_id_written_by_hand_with_a_create_gets_the_interface_alone()
    {
        var fixedSources = await Fix(
            Ids("public readonly record struct TenantId(long Value) : DDDToolkit.Abstractions.Interfaces.IEntityId<long>\n{\n    public static TenantId Create() => new(1);\n}"),
            "Add ICreatableEntityId<TenantId> to 'TenantId'");

        fixedSources["Ids.cs"].Should()
            .Contain("public readonly record struct TenantId(long Value) : DDDToolkit.Abstractions.Interfaces.IEntityId<long>, ICreatableEntityId<TenantId>")
            .And.Contain("public static TenantId Create() => new(1);")
            .And.NotContain("NotImplementedException", "the id's own Create() stays the only one");
    }

    [Fact]
    public async Task No_fix_is_offered_for_an_id_whose_create_does_not_fit()
    {
        var (document, diagnostics) = await Open(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ids.cs"] = Ids("[EntityId<long>]\npublic readonly partial record struct TenantId\n{\n    internal static TenantId Create() => new(1);\n}"),
            ["Classes.cs"] = Classes,
        });

        var actions = new List<CodeAction>();
        await new MissingCreateCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[0], (action, _) => actions.Add(action), CancellationToken.None));

        actions.Should().BeEmpty("a second Create() beside the id's own would not compile: making that one public is the author's to do");
    }

    [Fact]
    public async Task No_fix_is_offered_for_a_diagnostic_without_the_ids_name()
    {
        var (document, diagnostics) = await Open(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ids.cs"] = Ids("[EntityId<long>]\npublic readonly partial record struct TenantId;"),
            ["Classes.cs"] = Classes,
        });
        var stripped = Diagnostic.Create(diagnostics[0].Descriptor, diagnostics[0].Location);

        var actions = new List<CodeAction>();
        await new MissingCreateCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, stripped, (action, _) => actions.Add(action), CancellationToken.None));

        actions.Should().BeEmpty("the generator names the id; a fix that guessed it could write into the wrong type");
    }

    /// <summary>Applies the one fix offered for DDD00067, which is called <paramref name="title"/>, and returns every file after it.</summary>
    private static async Task<Dictionary<string, string>> Fix(string ids, string title = "Add a Create() to 'TenantId'")
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["Ids.cs"] = ids, ["Classes.cs"] = Classes };
        var (document, diagnostics) = await Open(sources);

        var actions = new List<CodeAction>();
        await new MissingCreateCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[0], (action, _) => actions.Add(action), CancellationToken.None));
        actions.Should().ContainSingle().Which.Title.Should().Be(title);

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        var fixedSources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var each in solution.Projects.Single().Documents)
        {
            fixedSources[each.Name] = (await each.GetTextAsync()).ToString().Replace("\r\n", "\n");
        }

        var rerun = Host(fixedSources).RunCore();
        rerun.ShouldNotHaveDiagnostic("DDD00067");
        rerun.ShouldCompile();
        return fixedSources;
    }

    private static GeneratorTestHost Host(Dictionary<string, string> sources)
        => GeneratorTestHost.Create(sources["Ids.cs"].Replace("\r\n", "\n"), "Ids.cs").WithSource(sources["Classes.cs"].Replace("\r\n", "\n"), "Classes.cs").WithTenancy();

    /// <summary>
    /// Runs the generators over the files and opens them as a workspace project, with DDD00067 anchored in Classes.cs,
    /// where the class names the id, as the IDE maps a generator's diagnostic back to the document by path and span.
    /// </summary>
    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> Open(Dictionary<string, string> sources)
    {
        var host = Host(sources);
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

        Document? classes = null;
        foreach (var (path, text) in sources)
        {
            var document = workspace.AddDocument(project.Id, path, SourceText.From(text.Replace("\r\n", "\n")));
            if (path == "Classes.cs")
            {
                classes = document;
            }
        }

        var tree = (await classes!.GetSyntaxTreeAsync())!;
        var diagnostics = outcome.GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == "DDD00067" && diagnostic.Location.GetLineSpan().Path == "Classes.cs")
            .Select(diagnostic => Diagnostic.Create(diagnostic.Descriptor, Location.Create(tree, diagnostic.Location.SourceSpan), diagnostic.Properties))
            .ToImmutableArray();

        diagnostics.Should().ContainSingle("the tenant names its id, and no other class of Tenancy's makes one over it");
        return (workspace.CurrentSolution.GetDocument(classes.Id)!, diagnostics);
    }
}
