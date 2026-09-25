using System.Collections.Immutable;
using DDDToolkit.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.CodeFixes;

/// <summary>
/// The fixes for DDD00034 (remove the <c>Version</c> the class name contradicts) and DDD00036 (pin another
/// name on one of two events that share one). Like the other fix tests, each runs the real generators for the
/// diagnostics, applies the fix through a workspace the way an IDE does, including the clean-up that adds
/// usings and shortens names, and runs the generators again: a fix is right when the diagnostic is gone and
/// the code still compiles.
/// </summary>
public class EventNameCodeFixTests
{
    // ------------------------------------------------------------------ DDD00034

    [Theory]
    [InlineData("[IntegrationEvent(Version = 3)]", "[IntegrationEvent]")]
    [InlineData("[IntegrationEvent(\"ordering.placed\", Version = 3)]", "[IntegrationEvent(\"ordering.placed\")]")]
    public async Task The_version_fix_removes_Version_and_leaves_the_name_to_say_it(string before, string after)
    {
        var source =
            $$"""
            using DDDToolkit.Abstractions.Attributes;

            namespace Ordering.Contracts;

            {{before}}
            public sealed record OrderPlacedV2(string OrderId);
            """;

        var fixedSource = await Fix(new EventVersionCodeFixProvider(), "DDD00034", source);

        fixedSource.Should().Be(Lf(source.Replace(before, after)));
    }

    // ------------------------------------------------------------------ DDD00036

    private const string TwoDomainEvents =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.BaseTypes;

        [assembly: Module("Ordering")]

        namespace Ordering.Orders
        {
            public sealed record OrderPlaced(string OrderId) : DomainEvent;
        }

        namespace Ordering.Returns
        {
            /// <summary>A return was placed.</summary>
            public sealed record OrderPlaced(string OrderId) : DomainEvent;
        }
        """;

    [Fact]
    public async Task The_name_fix_pins_a_domain_event_with_DomainEventName()
    {
        var fixedSource = await Fix(new EventNameCodeFixProvider(), "DDD00036", TwoDomainEvents, at: "Returns");

        fixedSource.Should().Contain(Lf(
            """
                /// <summary>A return was placed.</summary>
                [DomainEventName("ordering.returns-order-placed")]
                public sealed record OrderPlaced(string OrderId) : DomainEvent;
            """), "the attribute goes under the doc comment, where an author would write it");
        fixedSource.Should().Contain(Lf(
            """
            namespace Ordering.Orders
            {
                public sealed record OrderPlaced(string OrderId) : DomainEvent;
            }
            """), "only the event the fix was invoked on changes");
    }

    [Fact]
    public async Task The_name_fix_adds_the_using_it_needs()
    {
        var source = Lf(TwoDomainEvents).Replace("using DDDToolkit.Abstractions.Attributes;\n", "").Replace("[assembly: Module(\"Ordering\")]", "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Ordering\")]");

        var fixedSource = await Fix(new EventNameCodeFixProvider(), "DDD00036", source, at: "Returns");

        fixedSource.Should().StartWith(Lf("using DDDToolkit.Abstractions.Attributes;\n"));
        fixedSource.Should().Contain("[DomainEventName(\"ordering.returns-order-placed\")]");
    }

    [Theory]
    [InlineData("[IntegrationEvent]", "[IntegrationEvent(\"ordering.legacy-order-placed\")]")]
    [InlineData("[IntegrationEvent(Version = 1)]", "[IntegrationEvent(\"ordering.legacy-order-placed\", Version = 1)]")]
    public async Task The_name_fix_pins_a_contract_in_its_IntegrationEvent(string before, string after)
    {
        var source =
            $$"""
            using DDDToolkit.Abstractions.Attributes;

            [assembly: Module("Ordering")]

            namespace Ordering.Contracts
            {
                [IntegrationEvent]
                public sealed record OrderPlacedV1(string OrderId);
            }

            namespace Ordering.Legacy
            {
                {{before}}
                public sealed record OrderPlaced(string OrderId);
            }
            """;

        var fixedSource = await Fix(new EventNameCodeFixProvider(), "DDD00036", source, at: "Legacy");

        fixedSource.Should().Be(Lf(source).Replace("    " + before + "\n    public sealed record OrderPlaced(", "    " + after + "\n    public sealed record OrderPlaced("));
    }

    [Fact]
    public async Task A_pinned_name_is_offered_no_fix()
    {
        const string Source =
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.BaseTypes;

            [assembly: Module("Ordering")]

            namespace Ordering;

            [DomainEventName("ordering.placed")]
            public sealed record OrderPlaced(string OrderId) : DomainEvent;

            [DomainEventName("ordering.placed")]
            public sealed record PlacedOrder(string OrderId) : DomainEvent;
            """;

        var (document, diagnostics) = await Open(Source, "DDD00036");
        diagnostics.Should().HaveCount(2);

        var actions = new List<CodeAction>();
        foreach (var diagnostic in diagnostics)
        {
            await new EventNameCodeFixProvider().RegisterCodeFixesAsync(
                new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        }

        actions.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// Applies the one fix offered for the diagnostic whose line mentions <paramref name="at"/> (or the only
    /// one) and returns the fixed text.
    /// </summary>
    private static async Task<string> Fix(CodeFixProvider provider, string id, string source, string? at = null)
    {
        var (document, diagnostics) = await Open(source, id);
        var text = Lf(source);
        var diagnostic = at is null
            ? diagnostics.Single()
            : diagnostics.Single(candidate => NamespaceAround(text, candidate.Location.SourceSpan.Start).Contains(at, StringComparison.Ordinal));

        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        actions.Should().ContainSingle();

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var fixedText = (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString().Replace("\r\n", "\n");

        var rerun = GeneratorTestHost.Create(fixedText).RunCore();
        rerun.ShouldNotHaveDiagnostic(id);
        rerun.ShouldCompile();

        return fixedText;
    }

    /// <summary>The nearest <c>namespace</c> line above a position, to pick one of a pair of diagnostics by it.</summary>
    private static string NamespaceAround(string text, int position)
    {
        var start = text.LastIndexOf("namespace ", position, StringComparison.Ordinal);
        return start < 0 ? string.Empty : text.Substring(start, text.IndexOf('\n', start) - start);
    }

    /// <summary>
    /// Runs the generators and opens the source as a workspace document, with the generator's diagnostics
    /// anchored in that document and their properties kept, since the name fix reads its suggestion there.
    /// </summary>
    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> Open(string source, string id)
    {
        var text = Lf(source);
        var host = GeneratorTestHost.Create(text);
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
        var document = workspace.AddDocument(project.Id, "Source.cs", SourceText.From(text));
        var tree = (await document.GetSyntaxTreeAsync())!;

        var diagnostics = outcome.GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == id)
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => Diagnostic.Create(diagnostic.Descriptor, Location.Create(tree, diagnostic.Location.SourceSpan), diagnostic.Properties))
            .ToImmutableArray();

        diagnostics.Should().NotBeEmpty($"the source should report {id}");
        return (document, diagnostics);
    }

    private static string Lf(string text) => text.Replace("\r\n", "\n");
}
