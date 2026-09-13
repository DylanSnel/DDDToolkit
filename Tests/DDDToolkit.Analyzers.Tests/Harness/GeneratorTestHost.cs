using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using CoreEntityGenerator = DDDToolkit.Analyzers.Generators.EntityGenerator;
using CoreEntityIdGenerator = DDDToolkit.Analyzers.Generators.EntityIdGenerator;
using CoreSingleValueObjectGenerator = DDDToolkit.Analyzers.Generators.SingleValueObjectGenerator;
using CoreValueObjectGenerator = DDDToolkit.Analyzers.Generators.ValueObjectGenerator;
using EfEntityGenerator = DDDToolkit.EntityFramework.Analyzers.Generators.EntityGenerator;
using EfSingleValueObjectConverterGenerator = DDDToolkit.EntityFramework.Analyzers.Generators.SingleValueObjectConverterGenerator;
using EfValueObjectGenerator = DDDToolkit.EntityFramework.Analyzers.Generators.ValueObjectGenerator;
using FvValueObjectGenerator = DDDToolkit.FluentValidation.Analyzers.Generators.ValueObjectGenerator;
using HcSingleValueObjectConverterGenerator = DDDToolkit.HotChocolate.Analyzers.Generators.SingleValueObjectConverterGenerator;

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

    /// <summary>The three generators in DDDToolkit.EntityFramework.Analyzers.</summary>
    public static IIncrementalGenerator[] EntityFrameworkGenerators() =>
    [
        new EfSingleValueObjectConverterGenerator(),
        new EfEntityGenerator(),
        new EfValueObjectGenerator(),
    ];

    /// <summary>The generator in DDDToolkit.FluentValidation.Analyzers.</summary>
    public static IIncrementalGenerator[] FluentValidationGenerators() => [new FvValueObjectGenerator()];

    /// <summary>The generator in DDDToolkit.HotChocolate.Analyzers.</summary>
    public static IIncrementalGenerator[] HotChocolateGenerators() => [new HcSingleValueObjectConverterGenerator()];

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

    /// <summary>Sets <c>build_property.DDD_Module</c>, the MSBuild property that names the generated extension methods.</summary>
    public GeneratorTestHost WithModule(string moduleName)
    {
        _globalOptions["build_property.DDD_Module"] = moduleName;
        return this;
    }

    /// <summary>
    /// Sets <c>build_property.DDD_AlwaysValidValueObjects</c>. The option is read into
    /// <c>DDDOptions</c> but no generator acts on it yet; the setter exists so a test can drive it the
    /// day one does.
    /// </summary>
    public GeneratorTestHost WithAlwaysValidValueObjects(bool value = true)
    {
        _globalOptions["build_property.DDD_AlwaysValidValueObjects"] = value ? "true" : "false";
        return this;
    }

    /// <summary>Adds EF Core to the snippet's references; the generators change what they emit when it is present.</summary>
    public GeneratorTestHost WithEntityFramework()
    {
        _extraReferences.AddRange(ReferenceSets.EntityFramework);
        return this;
    }

    /// <summary>Adds only <c>Microsoft.EntityFrameworkCore.Abstractions</c> (the assembly that declares <c>[BackingField]</c>).</summary>
    public GeneratorTestHost WithEntityFrameworkAbstractions()
    {
        _extraReferences.Add(ReferenceSets.EntityFrameworkAbstractions);
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

        var compilation = CSharpCompilation.Create(
            _assemblyName,
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

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
