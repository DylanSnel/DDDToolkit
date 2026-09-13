using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// The result of one generator run: what was generated, what the generator complained about, what the
/// compiler then said about the whole thing, and the driver that produced it.
/// </summary>
public sealed class GeneratorRunOutcome
{
    private readonly GeneratorTestHost _host;
    private readonly CSharpParseOptions _parseOptions;

    private GeneratorRunOutcome(
        GeneratorTestHost host,
        CSharpParseOptions parseOptions,
        GeneratorDriver driver,
        Compilation inputCompilation,
        Compilation outputCompilation,
        ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        _host = host;
        _parseOptions = parseOptions;
        Driver = driver;
        InputCompilation = inputCompilation;
        OutputCompilation = outputCompilation;
        GeneratorDiagnostics = generatorDiagnostics;
        CompilationDiagnostics = outputCompilation.GetDiagnostics();
        GeneratedSources = [.. driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources)];
    }

    internal static GeneratorRunOutcome Run(GeneratorDriver driver, Compilation compilation, CSharpParseOptions parseOptions, GeneratorTestHost host)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return new GeneratorRunOutcome(host, parseOptions, driver, compilation, outputCompilation, diagnostics);
    }

    /// <summary>The driver after the run; hand it back to <see cref="RunAgain"/> to test incremental caching.</summary>
    public GeneratorDriver Driver { get; }

    /// <summary>The compilation the generators saw.</summary>
    public Compilation InputCompilation { get; }

    /// <summary>The compilation including the generated sources.</summary>
    public Compilation OutputCompilation { get; }

    /// <summary>Diagnostics the generators reported (DDD000xx live here, not in <see cref="CompilationDiagnostics"/>).</summary>
    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

    /// <summary>Diagnostics the compiler reported over the user's code plus the generated code.</summary>
    public ImmutableArray<Diagnostic> CompilationDiagnostics { get; }

    /// <summary>Every source the generators added, in the order they were added.</summary>
    public ImmutableArray<GeneratedSourceResult> GeneratedSources { get; }

    /// <summary>Compiler errors only.</summary>
    public IEnumerable<Diagnostic> CompilationErrors
        => CompilationDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    // ------------------------------------------------------------------ generated source

    /// <summary>The generated source whose hint name contains <paramref name="hintNameFragment"/>.</summary>
    public string Source(string hintNameFragment)
    {
        var matches = GeneratedSources.Where(source => source.HintName.Contains(hintNameFragment, StringComparison.Ordinal)).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"No generated source with a hint name containing '{hintNameFragment}'. Generated: {HintNames}.");
        }

        return string.Join("\n", matches.Select(match => match.SourceText.ToString()));
    }

    /// <summary>Every generated source concatenated; handy for "does anything mention X".</summary>
    public string AllSources => string.Join("\n", GeneratedSources.Select(source => source.SourceText.ToString()));

    /// <summary>The hint names of the generated sources, comma separated (used in assertion messages).</summary>
    public string HintNames => GeneratedSources.Length == 0
        ? "(nothing)"
        : string.Join(", ", GeneratedSources.Select(source => source.HintName));

    // ------------------------------------------------------------------ assertions

    /// <summary>Asserts the generated code compiles: no generator errors and no compiler errors.</summary>
    public GeneratorRunOutcome ShouldCompile()
    {
        var generatorErrors = GeneratorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        var compilerErrors = CompilationErrors.ToList();
        if (generatorErrors.Count > 0 || compilerErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Expected the snippet and the generated code to compile, but:\n"
                + string.Join("\n", generatorErrors.Concat(compilerErrors).Select(Describe))
                + "\n\nGenerated sources: " + HintNames + "\n\n" + AllSources);
        }

        return this;
    }

    /// <summary>
    /// Asserts a diagnostic with this id was reported, and that it points at <paramref name="at"/> —
    /// the exact text the location spans, normally the type or property identifier.
    /// </summary>
    public Diagnostic ShouldHaveDiagnostic(string id, string at)
    {
        var candidates = GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == id).ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Expected diagnostic {id}, got: {DescribeAll(GeneratorDiagnostics)}");
        }

        var matching = candidates.Where(diagnostic => TextAt(diagnostic) == at).ToList();
        if (matching.Count == 0)
        {
            throw new InvalidOperationException(
                $"{id} was reported, but not at '{at}'. Locations: "
                + string.Join(", ", candidates.Select(diagnostic => $"'{TextAt(diagnostic)}'")));
        }

        return matching[0];
    }

    /// <summary>Asserts no diagnostic with this id was reported.</summary>
    public GeneratorRunOutcome ShouldNotHaveDiagnostic(string id)
    {
        var unexpected = GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == id).ToList();
        if (unexpected.Count > 0)
        {
            throw new InvalidOperationException($"Did not expect {id}, but got: {DescribeAll([.. unexpected])}");
        }

        return this;
    }

    /// <summary>Asserts exactly these diagnostic ids were reported (order-insensitive).</summary>
    public GeneratorRunOutcome ShouldHaveExactlyDiagnostics(params string[] ids)
    {
        var actual = GeneratorDiagnostics.Select(diagnostic => diagnostic.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var expected = ids.OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected exactly [{string.Join(", ", expected)}], got: {DescribeAll(GeneratorDiagnostics)}");
        }

        return this;
    }

    /// <summary>
    /// Asserts the generators produced nothing for this type: no hint name mentions it and no generated
    /// source declares it. This is the half of a diagnostic test that catches a silently skipped type.
    /// </summary>
    public GeneratorRunOutcome ShouldNotHaveGeneratedFor(string typeName)
    {
        var byHintName = GeneratedSources
            .Where(source => source.HintName.Contains(typeName, StringComparison.Ordinal))
            .ToList();
        if (byHintName.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected nothing generated for '{typeName}', but got {string.Join(", ", byHintName.Select(source => source.HintName))}:\n"
                + string.Join("\n", byHintName.Select(source => source.SourceText.ToString())));
        }

        var mentioning = GeneratedSources
            .Where(source => source.SourceText.ToString().Contains(typeName, StringComparison.Ordinal))
            .ToList();
        if (mentioning.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected nothing generated for '{typeName}', but it is named in {string.Join(", ", mentioning.Select(source => source.HintName))}.");
        }

        return this;
    }

    /// <summary>Asserts a source was generated whose hint name contains the fragment.</summary>
    public GeneratorRunOutcome ShouldHaveGenerated(string hintNameFragment)
    {
        if (!GeneratedSources.Any(source => source.HintName.Contains(hintNameFragment, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Expected a generated source matching '{hintNameFragment}'. Generated: {HintNames}.");
        }

        return this;
    }

    /// <summary>Asserts the generated source for <paramref name="hintNameFragment"/> contains this text.</summary>
    public GeneratorRunOutcome ShouldContain(string hintNameFragment, string expected)
    {
        var source = Source(hintNameFragment);
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{hintNameFragment}' does not contain:\n{expected}\n\nActual:\n{source}");
        }

        return this;
    }

    /// <summary>Asserts the generated source for <paramref name="hintNameFragment"/> does not contain this text.</summary>
    public GeneratorRunOutcome ShouldNotContain(string hintNameFragment, string unexpected)
    {
        var source = Source(hintNameFragment);
        if (source.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{hintNameFragment}' unexpectedly contains:\n{unexpected}\n\nActual:\n{source}");
        }

        return this;
    }

    // ------------------------------------------------------------------ running it

    /// <summary>
    /// Compiles the result to an in-memory assembly, loads it and returns a small reflection facade, so a
    /// test can exercise generated members (Parse, ToString, JSON, operators) end to end.
    /// </summary>
    public EmittedAssembly Emit()
    {
        ShouldCompile();

        using var stream = new MemoryStream();
        var result = OutputCompilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Emitting the compiled snippet failed:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Describe)));
        }

        return new EmittedAssembly(Assembly.Load(stream.ToArray()));
    }

    // ------------------------------------------------------------------ incremental runs

    /// <summary>
    /// Runs the same driver again over an edited copy of the compilation, so
    /// <c>GetRunResult().Results[..].TrackedOutputSteps</c> can be compared across runs.
    /// </summary>
    /// <param name="edit">Transforms the first run's input compilation into the second run's input.</param>
    public GeneratorRunOutcome RunAgain(Func<Compilation, CSharpParseOptions, Compilation> edit)
    {
        var edited = edit(InputCompilation, _parseOptions);
        return Run(Driver, edited, _parseOptions, _host);
    }

    /// <summary>
    /// Every reason reported by the source-output steps of every generator in this run. On a second run
    /// over an unrelated edit these must all be <see cref="IncrementalStepRunReason.Cached"/> or
    /// <see cref="IncrementalStepRunReason.Unchanged"/>, otherwise the generator re-ran for nothing.
    /// </summary>
    public IReadOnlyList<(string StepName, IncrementalStepRunReason Reason)> OutputStepReasons()
        =>
        [
            .. Driver.GetRunResult().Results
                .SelectMany(result => result.TrackedOutputSteps)
                .SelectMany(step => step.Value.SelectMany(run => run.Outputs.Select(output => (step.Key, output.Reason))))
        ];

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The source text a diagnostic points at. Diagnostics from these generators carry a rebuilt
    /// <see cref="Location"/> without a syntax tree (that is deliberate — holding a tree would break
    /// incremental caching), so the text is looked up through the file path instead.
    /// </summary>
    public string TextAt(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var tree = InputCompilation.SyntaxTrees.FirstOrDefault(candidate => candidate.FilePath == lineSpan.Path);
        if (tree is null)
        {
            return "(unknown file " + lineSpan.Path + ")";
        }

        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private string Describe(Diagnostic diagnostic)
        => $"{diagnostic.Id} {diagnostic.Severity} at '{TextAt(diagnostic)}' ({diagnostic.Location.GetLineSpan()}): {diagnostic.GetMessage()}";

    private string DescribeAll(ImmutableArray<Diagnostic> diagnostics)
        => diagnostics.Length == 0 ? "(none)" : "\n" + string.Join("\n", diagnostics.Select(Describe));
}
