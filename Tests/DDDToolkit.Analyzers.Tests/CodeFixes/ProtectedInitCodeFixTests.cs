using System.Collections.Immutable;
using DDDToolkit.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.CodeFixes;

/// <summary>
/// The code fix for DDD00010 and DDD00011. Every test runs the real generators to get the diagnostics,
/// applies the fix through a workspace the way an IDE does, and then runs the generators again over the
/// result: a fix is only right when the diagnostic is gone and the code still compiles.
/// </summary>
public class ProtectedInitCodeFixTests
{
    private const string Usings = "using DDDToolkit.Abstractions.Attributes;\n\nnamespace Sample;\n\n";

    [Fact]
    public async Task Fix_all_rewrites_every_setter_in_the_document()
    {
        var fixedSource = await FixAll(
            """
            [ValueObject]
            public partial record Address
            {
                public string Street { get; set; }

                public string City { get; init; }
            }
            """);

        fixedSource.Should().Be(Lf(
            """
            [ValueObject]
            public partial record Address
            {
                public string Street { get; protected init; }

                public string City { get; protected init; }
            }
            """));
    }

    [Theory]
    [InlineData("public decimal Amount { get; set; }", "public decimal Amount { get; protected init; }")]
    [InlineData("public decimal Amount { get; init; }", "public decimal Amount { get; protected init; }")]
    [InlineData("public decimal Amount { get; protected set; }", "public decimal Amount { get; protected init; }")]
    [InlineData("public decimal Amount { get; private init; }", "public decimal Amount { get; protected init; }")]
    [InlineData("internal decimal Amount { get; init; }", "internal decimal Amount { get; private protected init; }")]
    public async Task A_declared_setter_becomes_protected_init(string declaration, string expected)
    {
        var fixedSource = await Fix(
            $$"""
            [ValueObject]
            public partial record Money
            {
                {{declaration}}
            }
            """);

        fixedSource.Should().Contain(expected);
    }

    [Fact]
    public async Task A_private_property_gets_no_fix_because_its_setter_cannot_be_protected()
    {
        var actions = await Actions(
            """
            [ValueObject]
            public partial record Money
            {
                private decimal Amount { get; set; }
            }
            """);

        actions.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ harness

    /// <summary>Applies the fix offered for one diagnostic and returns the snippet without its usings.</summary>
    private static async Task<string> Fix(string snippet, int diagnosticIndex = 0)
    {
        var (document, diagnostics) = await Open(snippet);
        var diagnostic = diagnostics[diagnosticIndex];

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new ProtectedInitCodeFixProvider().RegisterCodeFixesAsync(context);

        actions.Should().ContainSingle();
        return await ApplyAndVerify(document, actions[0]);
    }

    private static async Task<string> FixAll(string snippet)
    {
        var (document, diagnostics) = await Open(snippet);
        var provider = new ProtectedInitCodeFixProvider();

        var context = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            "DDDToolkit.ProtectedInit",
            provider.FixableDiagnosticIds,
            new FixedDiagnostics(diagnostics),
            CancellationToken.None);

        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        action.Should().NotBeNull();
        return await ApplyAndVerify(document, action!);
    }

    private static async Task<List<CodeAction>> Actions(string snippet)
    {
        var (document, diagnostics) = await Open(snippet);
        diagnostics.Should().NotBeEmpty("the snippet should report the diagnostic the fix declines");

        var actions = new List<CodeAction>();
        foreach (var diagnostic in diagnostics)
        {
            var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
            await new ProtectedInitCodeFixProvider().RegisterCodeFixesAsync(context);
        }

        return actions;
    }

    /// <summary>
    /// Runs the generators over the snippet and opens it as a workspace document, with the DDD00010 and
    /// DDD00011 diagnostics anchored in that document. The generators report them at a rebuilt location
    /// without a syntax tree; the IDE maps those back to the document by path and span, and so does this.
    /// </summary>
    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> Open(string snippet)
    {
        var host = GeneratorTestHost.Create(Usings + snippet);
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
        var document = workspace.AddDocument(project.Id, "Source.cs", SourceText.From(Usings + snippet));
        var tree = (await document.GetSyntaxTreeAsync())!;

        var diagnostics = outcome.GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id is "DDD00010" or "DDD00011")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => Diagnostic.Create(diagnostic.Descriptor, Location.Create(tree, diagnostic.Location.SourceSpan)))
            .ToImmutableArray();

        return (document, diagnostics);
    }

    private static async Task<string> ApplyAndVerify(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var text = (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString().Replace("\r\n", "\n");

        var rerun = GeneratorTestHost.Create(text).RunCore();
        rerun.GeneratorDiagnostics.Should().BeEmpty("the fix should leave nothing for DDD00010 or DDD00011 to report:\n" + text);
        rerun.ShouldCompile();

        text.Should().StartWith(Usings);
        return text.Substring(Usings.Length);
    }

    /// <summary>The expected text with the line endings the fixed text is normalized to, whatever git checked out.</summary>
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private sealed class FixedDiagnostics(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>([]);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
