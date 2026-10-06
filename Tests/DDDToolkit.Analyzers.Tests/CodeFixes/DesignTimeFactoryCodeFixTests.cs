using System.Collections.Immutable;
using DDDToolkit.Analyzers.CodeFixes;
using DDDToolkit.Analyzers.Tests.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.CodeFixes;

/// <summary>
/// The code fix for DDD00071: a design-time factory whose context does not keep its migration history where the
/// application does gets <c>UseDDDToolkitDesignTime()</c> in front of the <c>Options</c> it reads from its builder, and
/// the <c>using</c> the call needs. Every test applies the fix through a workspace the way an IDE does and runs the
/// analyzer again over the result: a fix is only right when the warning is gone and the code still compiles.
/// </summary>
public class DesignTimeFactoryCodeFixTests
{
    [Fact]
    public async Task A_factory_written_as_one_expression_gets_the_call_before_Options_and_the_using()
    {
        var fixedText = await Fix(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options);
            }
            """);

        fixedText.Should().Contain("=> new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase(\"Host=unused\").UseDDDToolkitDesignTime().Options);")
            .And.StartWith("using System;\nusing DDDToolkit.EntityFramework;\nusing Microsoft.EntityFrameworkCore;\n", "the using goes where the sorted order puts it");
    }

    [Fact]
    public async Task A_factory_that_fills_a_builder_in_steps_gets_the_call_where_it_reads_the_options()
    {
        var fixedText = await Fix(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                {
                    var options = new DbContextOptionsBuilder<OrderingContext>();
                    options.UseShopDatabase("Host=unused");
                    return new OrderingContext(options.Options);
                }
            }
            """);

        fixedText.Should().Contain("return new OrderingContext(options.UseDDDToolkitDesignTime().Options);");
    }

    [Fact]
    public async Task A_chain_over_several_lines_gets_the_call_on_a_line_of_its_own()
    {
        var fixedText = await Fix(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>()
                        .UseShopDatabase("Host=unused")
                        .Options);
            }
            """);

        fixedText.Should().Contain(
            """
                    => new(new DbContextOptionsBuilder<OrderingContext>()
                        .UseShopDatabase("Host=unused")
                        .UseDDDToolkitDesignTime()
                        .Options);
            """.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task No_using_is_added_where_the_namespace_is_imported_already()
    {
        var fixedText = await Fix(
            """
            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args)
                    => new(new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options);
            }
            """,
            globalUsing: true);

        fixedText.Should().NotContain("using DDDToolkit.EntityFramework;").And.Contain(".UseDDDToolkitDesignTime().Options");
    }

    [Fact]
    public async Task No_fix_where_the_factory_reads_no_options_from_a_builder()
    {
        // The options come finished from somewhere else: where the call goes is the developer's to decide.
        var actions = await Actions(
            """
            public static class Elsewhere
            {
                public static DbContextOptions<OrderingContext> Options() => new DbContextOptionsBuilder<OrderingContext>().UseShopDatabase("Host=unused").Options;
            }

            public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
            {
                public OrderingContext CreateDbContext(string[] args) => new(Elsewhere.Options());
            }
            """);

        actions.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ harness

    /// <summary>Applies the one fix offered for the one diagnostic and returns the fixed text, checked by a second run.</summary>
    private static async Task<string> Fix(string code, bool globalUsing = false)
    {
        var (document, diagnostics) = await Open(code, globalUsing);
        var actions = await ActionsFor(document, diagnostics.Single());
        var chosen = actions.Should().ContainSingle().Subject;
        chosen.EquivalenceKey.Should().Be("DDDToolkit.UseDDDToolkitDesignTime");

        var operations = await chosen.GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var fixedText = (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString().Replace("\r\n", "\n");

        var rerun = Host(fixedText, globalUsing).RunCore();
        rerun.ShouldNotHaveDiagnostic("DDD00071");
        rerun.ShouldCompile();

        return fixedText;
    }

    private static async Task<List<CodeAction>> Actions(string code)
    {
        var (document, diagnostics) = await Open(code, globalUsing: false);
        return await ActionsFor(document, diagnostics.Single());
    }

    private static async Task<List<CodeAction>> ActionsFor(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new DesignTimeFactoryCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }

    /// <summary>The project the factory is in: DDDToolkit.EntityFramework referenced, its namespace imported or not.</summary>
    private static GeneratorTestHost Host(string text, bool globalUsing)
    {
        var host = GeneratorTestHost.Create(text).WithEntityFrameworkRuntime().WithAnalyzers(GeneratorTestHost.EntityFrameworkAnalyzers());
        return globalUsing ? host.WithSource(Usings, "Usings.cs") : host;
    }

    private const string Usings = "global using DDDToolkit.EntityFramework;";

    /// <summary>Runs the analyzer and opens the source as a workspace document, with DDD00071 anchored in that document.</summary>
    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> Open(string code, bool globalUsing)
    {
        var text = (DesignTimeFactoryDiagnosticTests.Ordering + code).Replace("\r\n", "\n");
        var host = Host(text, globalUsing);
        var outcome = host.RunCore();
        outcome.ShouldCompile();

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
        if (globalUsing)
        {
            project = workspace.AddDocument(project.Id, "Usings.cs", SourceText.From(Usings)).Project;
        }

        var document = workspace.AddDocument(project.Id, "Source.cs", SourceText.From(text));
        var tree = (await document.GetSyntaxTreeAsync())!;

        var diagnostics = outcome.AnalyzerDiagnostics
            .Where(diagnostic => diagnostic.Id == "DDD00071")
            .Where(diagnostic => diagnostic.Location.SourceTree?.FilePath == "Source.cs")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => Diagnostic.Create(diagnostic.Descriptor, Location.Create(tree, diagnostic.Location.SourceSpan), diagnostic.Properties))
            .ToImmutableArray();

        diagnostics.Should().NotBeEmpty("the source should report DDD00071");
        return (document, diagnostics);
    }
}
