using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using DDDToolkit.Analyzers.Analyzers;
using CoreAccessBehaviorGenerator = DDDToolkit.Analyzers.AccessBehaviorGenerator;
using CoreEntityGenerator = DDDToolkit.Analyzers.EntityGenerator;
using CoreEntityIdGenerator = DDDToolkit.Analyzers.EntityIdGenerator;
using CoreEventNamesGenerator = DDDToolkit.Analyzers.EventNamesGenerator;
using CoreModuleGenerator = DDDToolkit.Analyzers.ModuleGenerator;
using CoreRowAccessGenerator = DDDToolkit.Analyzers.RowAccessGenerator;
using CoreSingleValueObjectGenerator = DDDToolkit.Analyzers.SingleValueObjectGenerator;
using CoreTemplateFacadeGenerator = DDDToolkit.Analyzers.TemplateFacadeGenerator;
using CoreTemplateRegistrationGenerator = DDDToolkit.Analyzers.TemplateRegistrationGenerator;
using CoreValueObjectGenerator = DDDToolkit.Analyzers.ValueObjectGenerator;
using EfEntityGenerator = DDDToolkit.EntityFramework.Analyzers.EntityGenerator;
using EfIntegrationEventsGenerator = DDDToolkit.EntityFramework.Analyzers.IntegrationEventsGenerator;
using EfSingleValueObjectConverterGenerator = DDDToolkit.EntityFramework.Analyzers.SingleValueObjectConverterGenerator;
using EfValueObjectGenerator = DDDToolkit.EntityFramework.Analyzers.ValueObjectGenerator;
using FvValueObjectGenerator = DDDToolkit.FluentValidation.Analyzers.ValueObjectGenerator;
using HcSingleValueObjectConverterGenerator = DDDToolkit.HotChocolate.Analyzers.SingleValueObjectConverterGenerator;
using MemberListGenerator = DDDToolkit.Supporting.Membership.Analyzers.MemberListGenerator;
using MembershipWithTenancyGenerator = DDDToolkit.Supporting.Membership.EntityFramework.Analyzers.MembershipWithTenancyGenerator;
using SupabaseMigrationsGenerator = DDDToolkit.EntityFramework.Supabase.Analyzers.SupabaseMigrationsGenerator;
using TenancyPermissionsGenerator = DDDToolkit.Supporting.Tenancy.Analyzers.TenancyPermissionsGenerator;

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
    /// <summary>
    /// What the DDDToolkit.Analyzers package's props file gives every project: each property a generator or an
    /// analyzer reads, declared, and empty until the project sets it. And what its targets file gives:
    /// <c>DDD_DeclareModule</c>, declared, whose arrival says the build step that declares a module ran.
    /// <see cref="WithoutBuildProperties"/> takes them all away again, and <see cref="WithoutTheModuleStep"/> the last.
    /// </summary>
    private readonly Dictionary<string, string> _globalOptions = new(StringComparer.Ordinal)
    {
        ["build_property.DDD_Module"] = string.Empty,
        ["build_property.IsTestProject"] = string.Empty,
        ["build_property.IsTestingPlatformApplication"] = string.Empty,
        ["build_property.DDD_DeclareModule"] = string.Empty,
    };

    private readonly List<string> _noWarn = [];
    private readonly List<DiagnosticAnalyzer> _analyzers = [];
    private string _assemblyName = DefaultAssemblyName;
    private OutputKind _outputKind = OutputKind.DynamicallyLinkedLibrary;

    private GeneratorTestHost()
    {
    }

    /// <summary>A host over a single source file.</summary>
    public static GeneratorTestHost Create(string source, string path = "Source.cs")
        => new GeneratorTestHost().WithSource(source, path);

    /// <summary>The nine generators in DDDToolkit.Analyzers, in the order the compiler would run them.</summary>
    public static IIncrementalGenerator[] CoreGenerators() =>
    [
        new CoreEntityIdGenerator(),
        new CoreSingleValueObjectGenerator(),
        new CoreValueObjectGenerator(),
        new CoreEntityGenerator(),
        new CoreEventNamesGenerator(),
        new CoreRowAccessGenerator(),
        new CoreTemplateRegistrationGenerator(),
        new CoreTemplateFacadeGenerator(),
        new CoreAccessBehaviorGenerator(),
        new CoreModuleGenerator(),
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

    /// <summary>The generator in DDDToolkit.Supporting.Membership.Analyzers, which a project that declares a member class runs.</summary>
    public static IIncrementalGenerator[] MemberListGenerators() => [new MemberListGenerator()];

    /// <summary>The generator in DDDToolkit.Supporting.Membership.EntityFramework.Analyzers.</summary>
    public static IIncrementalGenerator[] MembershipGenerators() => [new MembershipWithTenancyGenerator()];

    /// <summary>
    /// The generator in DDDToolkit.Supporting.Tenancy.Analyzers, which collects the keys the modules a project references
    /// mark with <c>[TenancyPermissions]</c>, and reports a marked list it could not read where it is declared.
    /// </summary>
    public static IIncrementalGenerator[] TenancyGenerators() => [new TenancyPermissionsGenerator()];

    /// <summary>The diagnostic analyzers in DDDToolkit.Analyzers, as opposed to its generators.</summary>
    public static DiagnosticAnalyzer[] CoreAnalyzers() => [new ModuleBoundaryAnalyzer(), new InvariantAnalyzer(), new AccessRequestsAnalyzer(), new DirectHandlerCallAnalyzer()];

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
    /// Compiles the snippet as an application, what an <c>OutputType</c> of <c>Exe</c> gives the compiler, or of
    /// <c>WinExe</c> for <see cref="OutputKind.WindowsApplication"/>, with an entry point of its own in a file
    /// beside the snippet. Without it the snippet is a library, as every project is that does not say otherwise.
    /// </summary>
    public GeneratorTestHost AsApplication(OutputKind kind = OutputKind.ConsoleApplication)
    {
        _outputKind = kind;
        return WithSource("internal static class EntryPoint { private static void Main() { } }", "EntryPoint.cs");
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

    /// <summary>The MSBuild properties as the compiler hands them to generators and analyzers alike.</summary>
    internal AnalyzerConfigOptionsProvider OptionsProvider => new TestAnalyzerConfigOptionsProvider(GlobalOptions());

    /// <summary>
    /// Sets <c>build_property.DDD_Module</c>, the MSBuild property, and nothing more: how the generators of a project see
    /// it that the build does not declare a module, a test project or one that sets <c>DDD_DeclareModule</c> to false,
    /// where it names the generated extension methods and no more. <see cref="WithModuleFromTheBuild"/> is the
    /// project the property declares a module, and <see cref="WithoutTheModuleStep"/> one where nothing could.
    /// </summary>
    public GeneratorTestHost WithModule(string moduleName)
    {
        _globalOptions["build_property.DDD_Module"] = moduleName;
        return this;
    }

    /// <summary>
    /// Compiles the project the way the build of one that sets <c>DDD_Module</c> compiles it: with the file the targets
    /// of the DDDToolkit.Analyzers package write into it, which declares the module, read from those targets
    /// (<see cref="ModuleDeclarationFile"/>), and with the property itself, which the props file hands the generators.
    /// </summary>
    public GeneratorTestHost WithModuleFromTheBuild(string module)
        => WithModule(module).WithSource(ModuleDeclarationFile.For(module), "obj/Debug/net10.0/" + _assemblyName + ".DDDToolkitModule.g.cs");

    /// <summary>Sets any MSBuild property the way <c>CompilerVisibleProperty</c> exposes it: <c>build_property.{name}</c>.</summary>
    public GeneratorTestHost WithBuildProperty(string name, string value)
    {
        _globalOptions["build_property." + name] = value;
        return this;
    }

    /// <summary>
    /// Compiles the way a project does when the package's props file was not imported: no
    /// <c>build_property.*</c> key reaches the generators at all, which is not the same as a property
    /// that is declared and left empty. The targets file, which arrives with it, was not imported either.
    /// </summary>
    public GeneratorTestHost WithoutBuildProperties()
    {
        _globalOptions.Clear();
        return this;
    }

    /// <summary>
    /// Compiles the way a project does whose props file was imported and whose targets file was not: the build step
    /// that declares the module from <c>DDD_Module</c> never ran, and <c>DDD_DeclareModule</c>, which that file
    /// declares, does not reach the generators.
    /// </summary>
    public GeneratorTestHost WithoutTheModuleStep()
    {
        _globalOptions.Remove("build_property.DDD_DeclareModule");
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
    /// <paramref name="generators"/> run over that assembly beside the core ones, for a referenced
    /// assembly that has to carry an integration's generated code as well.
    /// </summary>
    public GeneratorTestHost WithReferencedAssembly(string source, string assemblyName = "DDDToolkit.Sample.Referenced", params IIncrementalGenerator[] generators)
        => WithReferencedProject(assemblyName, project => project.WithSource(source, assemblyName + ".cs"), generators);

    /// <summary>
    /// Compiles another project into an assembly of its own and references it, as <see cref="WithReferencedAssembly"/>
    /// does, for a project that needs more than one snippet or more than the core generators: a module's domain
    /// project of several files, or a project that references Entity Framework and runs its generators too. The
    /// project starts with this host's references; <paramref name="configure"/> adds its sources and anything else.
    /// </summary>
    /// <param name="assemblyName">The referenced assembly's name.</param>
    /// <param name="configure">Adds the project's sources, references and build properties.</param>
    /// <param name="alsoRun">Generators to run after the core ones, such as <see cref="EntityFrameworkGenerators"/>.</param>
    public GeneratorTestHost WithReferencedProject(string assemblyName, Func<GeneratorTestHost, GeneratorTestHost> configure, params IIncrementalGenerator[] alsoRun)
    {
        var other = new GeneratorTestHost().WithAssemblyName(assemblyName);
        other._extraReferences.AddRange(_extraReferences);
        other = configure(other);

        var outcome = other.RunCoreAnd(alsoRun);
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

    /// <summary>
    /// Adds DDDToolkit.Supporting.Tenancy, the package as it ships: its templates and parents, compiled with the
    /// generators, seen through metadata the way an application sees them.
    /// </summary>
    public GeneratorTestHost WithTenancy()
    {
        _extraReferences.AddRange(ReferenceSets.Tenancy);
        return this;
    }

    /// <summary>
    /// Adds DDDToolkit.Supporting.Tenancy.EntityFramework beside the package itself, with Entity Framework:
    /// what a project sees that stores an organization, or asks Tenancy's questions over a context of its own.
    /// </summary>
    public GeneratorTestHost WithTenancyOnEntityFramework()
    {
        _extraReferences.AddRange(ReferenceSets.TenancyOnEntityFramework);
        return this;
    }

    /// <summary>
    /// Adds DDDToolkit.Supporting.Membership and its Entity Framework package, as they ship, with Entity
    /// Framework itself: the member template and the registration an application gets closed over its classes.
    /// </summary>
    public GeneratorTestHost WithMembership()
    {
        _extraReferences.AddRange(ReferenceSets.Membership);
        return this;
    }

    /// <summary>
    /// Adds DDDToolkit.Supporting.Membership without its Entity Framework package: what a domain project sees,
    /// which declares a member class and stores nothing.
    /// </summary>
    public GeneratorTestHost WithMembershipAlone()
    {
        _extraReferences.Add(ReferenceSets.MembershipAlone);
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

    /// <summary>
    /// Adds the Mediator library's abstractions and the service collection, which is what a project that
    /// references the toolkit and the library sees: the generator writes a behavior for every
    /// <c>[AccessRequests]</c> interface, and its registration.
    /// </summary>
    public GeneratorTestHost WithMediator()
    {
        _extraReferences.AddRange(ReferenceSets.Mediator);
        return this;
    }

    /// <summary>Adds the Mediator library's abstractions and nothing else: a project that cannot see the service collection.</summary>
    public GeneratorTestHost WithMediatorAlone()
    {
        _extraReferences.Add(ReferenceSets.MediatorAlone);
        return this;
    }

    /// <summary>
    /// Adds the service collection without the Mediator library, for a snippet that brings a library of its
    /// own under that name.
    /// </summary>
    public GeneratorTestHost WithDependencyInjection()
    {
        _extraReferences.Add(ReferenceSets.DependencyInjection);
        return this;
    }

    /// <summary>
    /// Adds HotChocolate without DDDToolkit.HotChocolate: what the generator sees in a project whose copy of the
    /// toolkit's package does not have a type the generator would otherwise write a use of.
    /// </summary>
    public GeneratorTestHost WithHotChocolateAlone()
    {
        _extraReferences.AddRange(ReferenceSets.HotChocolateAlone);
        return this;
    }

    /// <summary>Runs the DDDToolkit.Analyzers generators.</summary>
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

        var options = new CSharpCompilationOptions(_outputKind, nullableContextOptions: NullableContextOptions.Enable);
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
            optionsProvider: OptionsProvider,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    /// <summary>
    /// The build properties the generators see. Where the props file is imported, the project's file is among them,
    /// as <c>MSBuildProjectFullPath</c> gives it: <see cref="ProjectFile"/>, unless a test set another.
    /// </summary>
    private Dictionary<string, string> GlobalOptions()
    {
        var options = new Dictionary<string, string>(_globalOptions, StringComparer.Ordinal);
        if (options.ContainsKey("build_property.DDD_Module"))
        {
            options.TryAdd("build_property.MSBuildProjectFullPath", ProjectFile);
        }

        return options;
    }

    /// <summary>The project file a project of this name is built from, where a diagnostic about its module points when no line of its source declares one.</summary>
    public string ProjectFile => "src/" + _assemblyName + "/" + _assemblyName + ".csproj";

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
