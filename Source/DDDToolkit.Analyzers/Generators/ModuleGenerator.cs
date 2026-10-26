using System;
using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes <c>[assembly: Module("Ordering")]</c> into a project whose build declared it a project of module
/// Ordering: its <c>DDD_Module</c> is <c>Ordering</c>. So a <c>Directory.Build.props</c> that names the module of
/// every project below a folder makes them that module's projects, and none of them needs a file that says so.
/// <para>
/// The build writes the declaration into the project as <c>AssemblyMetadata</c>, with the targets of the
/// DDDToolkit.Analyzers package, and every generator reads the module from there, through
/// <see cref="ModuleBoundary.ModuleOf"/>: a generator never sees what another one writes, so the attribute
/// written here would come too late for the others. It is written for what reads the compiled assembly: the
/// module boundary analyzer, the runtime, which names a domain event after the module of its assembly, and
/// reflection. A test project, and one that sets <c>DDD_DeclareModule</c> to false, are not declared by the build,
/// and get nothing from here.
/// </para>
/// <para>
/// It is written only where the project declares no <c>[assembly: Module]</c> at all, and that is why a generator
/// writes it and the build does not. The compilation a generator is handed holds every file the compiler compiles,
/// the project's own and the <c>AssemblyInfo.cs</c> its build wrote from an <c>AssemblyAttribute</c> item, and
/// nothing a generator wrote; no other generator writes the attribute, and this one writes one file. So a project
/// that declares its module in a file of its own, or through an <c>AssemblyAttribute</c>, gets nothing from here,
/// and the attribute is never declared twice (CS0579). The one already there wins, as <c>[assembly: Module]</c>
/// always does over <c>DDD_Module</c>. The build cannot make that choice: it cannot see what the source declares.
/// </para>
/// <para>
/// The same pass reports DDD00064 where <c>DDD_Module</c> reaches the generators and the build step that declares
/// the module did not run: the generators arrived without the package's targets file, as an analyzer assembly or a
/// project reference, and the property through the props file alone or a <c>CompilerVisibleProperty</c> of the
/// project's own. The code is then named after the module and the project is no module, which nothing else would
/// say. The targets hand the generators <c>DDD_DeclareModule</c>, set or empty, and that it arrives is how this pass
/// tells they were imported; where they were, what they decided is right, a test project and an opt-out included.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ModuleGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Read off the compilation, so it runs on every edit: the project's attributes, which only change with a
        // file that declares one or with the build's word. A name compares equal when it did not change, so the file
        // stays cached.
        var module = context.CompilationProvider.Select(static (compilation, _) => ToWrite(compilation));

        context.RegisterSourceOutput(module, static (production, name) =>
        {
            if (name is not null)
            {
                production.AddSource("Module.g.cs", SourceText.From(Write(name), Encoding.UTF8));
            }
        });

        // The name the build was meant to declare and did not: a plain value, so the report stays cached while it does
        // not change.
        var undeclared = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) => NotDeclared(pair.Left, pair.Right.GlobalOptions));

        context.RegisterSourceOutput(undeclared.Combine(context.ProjectFile()), static (production, data) =>
        {
            if (data.Left is { } name)
            {
                DiagnosticInfo.Create(DiagnosticDescriptors.ModuleNotDeclaredByTheBuild, data.Right, name).Report(production);
            }
        });
    }

    /// <summary>
    /// The project's <c>DDD_Module</c> when it reached the generators and nothing declared it, because the package's
    /// targets were not imported; null otherwise. The targets make <c>DDD_DeclareModule</c> a property the compiler
    /// hands on, so where they were imported it arrives, empty when the project did not set it; where they were not,
    /// it does not arrive at all. Without them the two marks of a test project still arrive with the props file, and
    /// a test project is never a module; with neither file there is no <c>DDD_Module</c> either, which is DDD00014.
    /// </summary>
    private static string? NotDeclared(Compilation compilation, AnalyzerConfigOptions options)
    {
        if (!options.TryGetValue("build_property.DDD_Module", out var name)
            || string.IsNullOrWhiteSpace(name)
            || options.TryGetValue("build_property.DDD_DeclareModule", out _)
            || IsTrue(options, "build_property.IsTestProject")
            || IsTrue(options, "build_property.IsTestingPlatformApplication")
            || ModuleBoundary.HasModuleAttribute(compilation.Assembly)
            || ModuleBoundary.BuildModuleOf(compilation.Assembly) is not null)
        {
            return null;
        }

        return name.Trim();
    }

    private static bool IsTrue(AnalyzerConfigOptions options, string key)
        => options.TryGetValue(key, out var value) && string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The module to write the attribute for: the one the build declared, where the project declares none itself.
    /// Without the attribute's type there is nothing to write it with: a project with the generators and without
    /// DDDToolkit.Abstractions. Its generators and the projects that reference it still read the build's word.
    /// </summary>
    private static string? ToWrite(Compilation compilation)
        => !ModuleBoundary.HasModuleAttribute(compilation.Assembly)
           && ModuleBoundary.BuildModuleOf(compilation.Assembly) is { } declared
           && compilation.GetTypeByMetadataName(KnownTypes.ModuleAttribute) is not null
            ? declared
            : null;

    private static string Write(string module)
    {
        var writer = new CodeWriter().Header();
        writer.Line("// The module this project's build declared: its DDD_Module. An [assembly: Module] of the project's");
        writer.Line("// own would have been kept instead, and nothing written here.");
        writer.Line("[assembly: global::DDDToolkit.Abstractions.Attributes.ModuleAttribute(" + SymbolDisplay.FormatLiteral(module, quote: true) + ")]");
        return writer.ToString();
    }
}
