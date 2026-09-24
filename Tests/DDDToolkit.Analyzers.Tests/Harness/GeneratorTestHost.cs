using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using DDDToolkit.Analyzers.Analyzers;
using CoreEntityGenerator = DDDToolkit.Analyzers.EntityGenerator;
using CoreEntityIdGenerator = DDDToolkit.Analyzers.EntityIdGenerator;
using CoreSingleValueObjectGenerator = DDDToolkit.Analyzers.SingleValueObjectGenerator;
using CoreValueObjectGenerator = DDDToolkit.Analyzers.ValueObjectGenerator;
using EfEntityGenerator = DDDToolkit.EntityFramework.Analyzers.EntityGenerator;
using EfIntegrationEventsGenerator = DDDToolkit.EntityFramework.Analyzers.IntegrationEventsGenerator;
using EfSingleValueObjectConverterGenerator = DDDToolkit.EntityFramework.Analyzers.SingleValueObjectConverterGenerator;
using EfValueObjectGenerator = DDDToolkit.EntityFramework.Analyzers.ValueObjectGenerator;
using FvValueObjectGenerator = DDDToolkit.FluentValidation.Analyzers.ValueObjectGenerator;
using HcSingleValueObjectConverterGenerator = DDDToolkit.HotChocolate.Analyzers.SingleValueObjectConverterGenerator;
using SupabaseMigrationsGenerator = DDDToolkit.EntityFramework.Supabase.Analyzers.SupabaseMigrationsGenerator;

namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// Compiles a C# snippet against a real .NET 10 reference set plus the DDDToolkit runtime assemblies,
/// runs one or more DDDToolkit source generators over it and hands back everything a test needs to
/// judge the result: the generated sources, the generator's own diagnostics, the diagnostics of the
/// compilation <em>after</em> generation, the driver (for incremental-caching assertions) and — through
/// <see cref="GeneratorRunOutcome.Emit"/> — a loaded assembly whose generated members can be invoked.
/// </summary>
/// <remarks>
/// <para>Typical use:</para>
/// <code>
/// var result = GeneratorTestHost.Create(source).RunCore();
/// result.ShouldCompile();
/// result.ShouldContain("CatId", "public static CatId CreateUnique()");
/// </code>
/// <para>And for a misuse:</para>
/// <code>
/// var result = GeneratorTestHost.Create(source).RunCore();
/// result.ShouldHaveDiagnostic("DDD00001", at: "Money");
/// result.ShouldNotHaveGeneratedFor("Money");
/// </code>
/// </remarks>
public sealed class GeneratorTestHost
{
    /// <summary>Assembly name of the compiled snippet. Generated registration namespaces derive from it.</summary>
    public const string DefaultAssemblyName = "DDDToolkit.Sample";

    /// <summary>
    /// The symbols the .NET 10 SDK defines for a <c>net10.0</c> project. The generators emit code guarded
    /// by <c>#if NET9_0_OR_GREATER</c> (for <c>Guid.CreateVersion7</c>), so tests would silently lose that
    /// code without them.
    /// </summary>
    public static readonly ImmutableArray<string> PreprocessorSymbols =
    [
        "NET10_0",
        "NET10_0_OR_GREATER",
        "NET9_0_OR_GREATER",
        "NET8_0_OR_GREATER",
        "NET7_0_OR_GREATER",
        "NET6_0_OR_GREATER",
        "NET5_0_OR_GREATER",
        "NETCOREAPP",
    ];

    private readonly List<(string Path, string Text)> _sources = [];
    private readonly List<PortableExecutableReference> _extraReferences = [];
    private readonly Dictionary<string, string> _globalOptions = new(StringComparer.Ordinal);
    private readonly List<string> _noWarn = [];
    private readonly List<DiagnosticAnalyzer> _analyzers = [];
    private string _assemblyName = DefaultAssemblyName;

    private GeneratorTestHost()
    {
    }

    /// <summary>A host over a single source file.</summary>
    public static GeneratorTestHost Create(string source, string path = "Source.cs")
        => new GeneratorTestHost().WithSource(source, path);

    /// <summary>The four generators in DDDToolkit.Analyzers, in the order the compiler would run them.</summary>
    public static IIncrementalGenerator[] CoreGenerators() =>
    [
        new CoreEntityIdGenerator(),
        new CoreSingleValueObjectGenerator(),
        new CoreValueObjectGenerator(),
        new CoreEntityGenerator(),
    ];

    /// <summary>The generators in DDDToolkit.EntityFramework.Analyzers.</summary>
    public static IIncrementalGenerator[] EntityFrameworkGenerators() =>
    [
        new EfSingleValueObjectConverterGenerator(),
        new EfEntityGenerator(),
        new EfValueObjectGenerator(),
        new EfIntegrationEventsGenerator(),
    ];

    /// <summary>The generator in DDDToolkit.FluentValidation.Analyzers.</summary>
    public static IIncrementalGenerator[] FluentValidationGenerators() => [new FvValueObjectGenerator()];

    /// <summary>The generator in DDDToolkit.HotChocolate.Analyzers.</summary>
    public static IIncrementalGenerator[] HotChocolateGenerators() => [new HcSingleValueObjectConverterGenerator()];

    /// <summary>The generator in DDDToolkit.EntityFramework.Supabase.Analyzers.</summary>
    public static IIncrementalGenerator[] SupabaseGenerators() => [new SupabaseMigrationsGenerator()];

    /// <summary>The diagnostic analyzers in DDDToolkit.Analyzers, as opposed to its generators.</summary>
    public static DiagnosticAnalyzer[] CoreAnalyzers() => [new ModuleBoundaryAnalyzer(), new InvariantAnalyzer()];

    public GeneratorTestHost WithSource(string source, string path = "Source.cs")
    {
        _sources.Add((path, source));
        return this;
    }

    /// <summary>Overrides the assembly name; the generated <c>Add{Module}Converters</c> namespace derives from it.</summary>
    public GeneratorTestHost WithAssemblyName(string assemblyName)
    {
        _assemblyName = assemblyName;
        return this;
    }

    /// <summary>
    /// Suppresses these diagnostic ids the way an MSBuild <c>&lt;NoWarn&gt;</c> does, by putting them in the
    /// compilation's specific diagnostic options. That is the one route that reaches a source generator's
    /// diagnostics: a <c>#pragma warning disable</c> cannot, because these diagnostics carry a rebuilt
    /// location with no syntax tree for the compiler to match the pragma against.
    /// </summary>
    public GeneratorTestHost WithNoWarn(params string[] diagnosticIds)
    {
        _noWarn.AddRange(diagnosticIds);
        return this;
    }

    /// <summary>
    /// Runs these diagnostic analyzers over the compilation <em>after</em> generation, the way the
    /// compiler does, so an analyzer sees the generated partial parts and generated identifiers as well
    /// as the snippet. Their diagnostics join the generators' in every assertion on the outcome.
    /// </summary>
    public GeneratorTestHost WithAnalyzers(params DiagnosticAnalyzer[] analyzers)
    {
        _analyzers.AddRange(analyzers);
        return this;
    }

    /// <summary>The analyzers <see cref="WithAnalyzers"/> added, run by the outcome after generation.</summary>
    internal IReadOnlyList<DiagnosticAnalyzer> Analyzers => _analyzers;

    /// <summary>Sets <c>build_property.DDD_Module</c>, the MSBuild property that names the generated extension methods.</summary>
    public GeneratorTestHost WithModule(string moduleName)
    {
        _globalOptions["build_property.DDD_Module"] = moduleName;
        return this;
    }

    /// <summary>Sets any MSBuild property the way <c>CompilerVisibleProperty</c> exposes it: <c>build_property.{name}</c>.</summary>
    public GeneratorTestHost WithBuildProperty(string name, string value)
    {
        _globalOptions["build_property." + name] = value;
        return this;
    }

    /// <summary>Adds EF Core and DDDToolkit.EntityFramework.Supabase, for a <c>[SupabaseMigrations]</c> factory.</summary>
    public GeneratorTestHost WithSupabase()
    {
        _extraReferences.AddRange(ReferenceSets.Supabase);
        return this;
    }

    /// <summary>Adds EF Core to the snippet's references; the generators change what they emit when it is present.</summary>
    public GeneratorTestHost WithEntityFramework()
    {
        _extraReferences.AddRange(ReferenceSets.EntityFramework);
        return this;
    }

    /// <summary>
    /// Adds EF Core and DDDToolkit.EntityFramework itself, whose outbox and integration event types the
    /// integration event registration is written against.
    /// </summary>
    public GeneratorTestHost WithEntityFrameworkRuntime()
    {
        _extraReferences.AddRange(ReferenceSets.EntityFrameworkRuntime);
        return this;
    }

    /// <summary>Adds only <c>Microsoft.EntityFrameworkCore.Abstractions</c> (the assembly that declares <c>[BackingField]</c>).</summary>
    public GeneratorTestHost WithEntityFrameworkAbstractions()
    {
        _extraReferences.Add(ReferenceSets.EntityFrameworkAbstractions);
        return this;
    }

    /// <summary>
    /// Compiles another snippet into an assembly of its own, generators and all, and references it. Use it
    /// to put a DDDToolkit type behind an assembly boundary, where the generators see it through metadata
    /// rather than through source: attributes survive that trip, declaration syntax does not.
    /// </summary>
    public GeneratorTestHost WithReferencedAssembly(string source, string assemblyName = "DDDToolkit.Sample.Referenced")
    {
        var other = Create(source, assemblyName + ".cs").WithAssemblyName(assemblyName);
        other._extraReferences.AddRange(_extraReferences);

        var outcome = other.RunCore();
        outcome.ShouldCompile();

        using var stream = new MemoryStream();
        var emit = outcome.OutputCompilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(
                $"The referenced assembly '{assemblyName}' did not emit:\n"
                + string.Join("\n", emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }

        _extraReferences.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        return this;
    }

    public GeneratorTestHost WithFluentValidation()
    {
        _extraReferences.AddRange(ReferenceSets.FluentValidation);
        return this;
    }

    public GeneratorTestHost WithHotChocolate()
    {
        _extraReferences.AddRange(ReferenceSets.HotChocolate);
        return this;
    }

    /// <summary>Runs the four DDDToolkit.Analyzers generators.</summary>
    public GeneratorRunOutcome RunCore() => Run(CoreGenerators());

    /// <summary>Runs the core generators plus the given integration generators (the integrations build on the core output).</summary>
    public GeneratorRunOutcome RunCoreAnd(params IIncrementalGenerator[] generators)
        => Run([.. CoreGenerators(), .. generators]);

    /// <summary>Compiles the sources and runs the given generators over them.</summary>
    public GeneratorRunOutcome Run(params IIncrementalGenerator[] generators)
    {
        var parseOptions = CreateParseOptions();
        var trees = _sources
            .Select(source => CSharpSyntaxTree.ParseText(SourceText.From(source.Text, System.Text.Encoding.UTF8), parseOptions, source.Path))
            .ToImmutableArray();

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        if (_noWarn.Count > 0)
        {
            options = options.WithSpecificDiagnosticOptions(
                _noWarn.ToImmutableDictionary(id => id, _ => ReportDiagnostic.Suppress, StringComparer.Ordinal));
        }

        var compilation = CSharpCompilation.Create(_assemblyName, trees, References, options);

        var driver = CreateDriver(generators, parseOptions);
        return GeneratorRunOutcome.Run(driver, compilation, parseOptions, this);
    }

    /// <summary>
    /// Creates the driver used by <see cref="Run"/>. Exposed so incremental tests can keep a driver
    /// across two runs and inspect <c>TrackedOutputSteps</c>.
    /// </summary>
    public GeneratorDriver CreateDriver(IIncrementalGenerator[] generators, CSharpParseOptions parseOptions)
        => CSharpGeneratorDriver.Create(
            generators.Select(GeneratorExtensions.AsSourceGenerator).ToImmutableArray(),
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(_globalOptions),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    public CSharpParseOptions CreateParseOptions()
        => new CSharpParseOptions(LanguageVersion.Latest).WithPreprocessorSymbols(PreprocessorSymbols);

    /// <summary>Every reference the snippet compiles against.</summary>
    public IReadOnlyList<PortableExecutableReference> References
        => [.. ReferenceSets.Core, .. _extraReferences.Distinct()];

    private sealed class TestAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;

        private static AnalyzerConfigOptions Empty { get; } = new Options([]);

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);

            public override IEnumerable<string> Keys => values.Keys;
        }
    }
}
