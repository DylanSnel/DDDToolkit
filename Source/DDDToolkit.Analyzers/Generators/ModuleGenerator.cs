using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes <c>[assembly: Module("Ordering")]</c> into a project whose build declared it a project of module
/// Ordering: its <c>DDD_Module</c> is <c>Ordering</c> and its <c>DDD_DeclareModule</c> is true. So a
/// <c>Directory.Build.props</c> that names the module of every project below a folder makes them that module's
/// projects, and none of them needs a file that says so.
/// <para>
/// The build writes the declaration into the project as <c>AssemblyMetadata</c>, with the targets of the
/// DDDToolkit.Analyzers package, and every generator reads the module from there, through
/// <see cref="ModuleBoundary.ModuleOf"/>: a generator never sees what another one writes, so the attribute
/// written here would come too late for the others. It is written for what reads the compiled assembly: the
/// module boundary analyzer, the runtime, which names a domain event after the module of its assembly, and
/// reflection.
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
/// The same pass reports DDD00064: this project and one it references carry one module's name in <c>DDD_Module</c>,
/// and the generators do not take them together. Only the referenced one declares the module, and this one is left
/// out of what is written for it; only this one declares it, and the other's classes are missing here; or neither
/// declares it, and a package's registration that this project can call and the other cannot is written for the
/// other's template classes nowhere. The build writes a project's <c>DDD_Module</c> into it, declared or not, which
/// is how a project that references it can tell. A project that says it is no module, with <c>DDD_DeclareModule</c>
/// set to false, and a test project, which the build never declares, are not reported, and neither is a pair of
/// projects without templates that declare no module: that is how an application without modules names its code.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ModuleGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Read off the compilation, so it runs on every edit: the project's attributes and those of the assemblies it
        // references, which the compiler keeps for as long as the references do not change. What comes out compares
        // equal when nothing it says changed, so the file and the diagnostics stay cached.
        var module = context.CompilationProvider.Select(static (compilation, cancellationToken) => Read(compilation, cancellationToken));

        context.RegisterSourceOutput(module.Combine(context.ProjectFile()), static (production, data) =>
        {
            var (found, projectFile) = data;

            if (found.Write is { } name)
            {
                production.AddSource("Module.g.cs", SourceText.From(Write(name), Encoding.UTF8));
            }

            foreach (var pair in found.LeftOut)
            {
                var (declared, consequence, declareIn) = pair.Who switch
                {
                    LeftOutSide.ThisProject => (
                        "only that project declares the module",
                        "the generators do not take this project for one of the module's projects, and nothing is written here from what the others declare",
                        "this project"),
                    LeftOutSide.ThatProject => (
                        "only this project declares the module",
                        "the generators do not take that project for one of the module's projects, and its ids, domain events and template classes are left out of what is written here",
                        "that project"),
                    _ => (
                        "neither declares the module",
                        "the generators take neither for one of the module's projects, and nothing is written here for the classes that project declares with a template: " + string.Join(", ", pair.Registrations),
                        "both"),
                };

                DiagnosticInfo.Create(
                    DiagnosticDescriptors.ModuleNotDeclaredByEveryProject,
                    projectFile,
                    pair.Other,
                    pair.Module,
                    declared,
                    consequence,
                    declareIn).Report(production);
            }
        });
    }

    private static ModuleFacts Read(Compilation compilation, CancellationToken cancellationToken)
    {
        var assembly = compilation.Assembly;
        var build = ModuleBoundary.HasModuleAttribute(assembly) ? null : ModuleBoundary.BuildModuleOf(assembly);
        var module = ModuleBoundary.ModuleOf(assembly);

        // Without the attribute's type there is nothing to write it with: a project with the generators and without
        // DDDToolkit.Abstractions. Its generators and the projects that reference it still read the build's word.
        var write = build is { Declares: true } declared && compilation.GetTypeByMetadataName(KnownTypes.ModuleAttribute) is not null
            ? declared.Name
            : null;

        var leftOut = new List<LeftOutPair>();
        if (module is null && build is { Declares: null } named)
        {
            // This project carries a module's name and does not declare it. Told once about the first project, by
            // name, that declares the module; failing that, about each that declares no module either and whose
            // template classes a registration here would be closed over, were the two one module's projects.
            string? declaring = null;
            var undeclared = new List<LeftOutPair>();
            foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(ModuleBoundary.ModuleOf(referenced), named.Name, StringComparison.Ordinal))
                {
                    if (declaring is null || string.CompareOrdinal(referenced.Identity.Name, declaring) < 0)
                    {
                        declaring = referenced.Identity.Name;
                    }
                }
                else if (declaring is null
                    && SharesTheUndeclaredName(referenced, named.Name)
                    && TemplateRegistrations.WrittenForNobody(compilation, referenced, cancellationToken) is { Count: > 0 } registrations)
                {
                    undeclared.Add(new LeftOutPair(referenced.Identity.Name, named.Name, LeftOutSide.Both, registrations));
                }
            }

            if (declaring is not null)
            {
                leftOut.Add(new LeftOutPair(declaring, named.Name, LeftOutSide.ThisProject, EquatableArray<string>.Empty));
            }
            else
            {
                leftOut.AddRange(undeclared);
            }
        }
        else if (module is not null)
        {
            // This project declares the module, and references projects that carry its name and do not.
            foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (SharesTheUndeclaredName(referenced, module))
                {
                    leftOut.Add(new LeftOutPair(referenced.Identity.Name, module, LeftOutSide.ThatProject, EquatableArray<string>.Empty));
                }
            }
        }

        leftOut.Sort(static (left, right) => string.CompareOrdinal(left.Other, right.Other));
        return new ModuleFacts(write, leftOut.ToEquatableArray());
    }

    /// <summary>
    /// Whether a referenced project carries <paramref name="name"/> in <c>DDD_Module</c>, as the build wrote it, and
    /// declares no module, with the build saying nothing either way: not set to false, and no test project.
    /// </summary>
    private static bool SharesTheUndeclaredName(IAssemblySymbol referenced, string name)
        => !ModuleBoundary.HasModuleAttribute(referenced)
           && ModuleBoundary.BuildModuleOf(referenced) is { Declares: null } other
           && string.Equals(other.Name, name, StringComparison.Ordinal);

    private static string Write(string module)
    {
        var writer = new CodeWriter().Header();
        writer.Line("// The module this project's build declared: its DDD_Module, with DDD_DeclareModule set to true. An");
        writer.Line("// [assembly: Module] of the project's own would have been kept instead, and nothing written here.");
        writer.Line("[assembly: global::DDDToolkit.Abstractions.Attributes.ModuleAttribute(" + SymbolDisplay.FormatLiteral(module, quote: true) + ")]");
        return writer.ToString();
    }

    /// <param name="Write">The module to write <c>[assembly: Module]</c> for, or null when there is nothing to write.</param>
    /// <param name="LeftOut">What DDD00064 says, a pair of projects at a time, by the other project's name.</param>
    private sealed record ModuleFacts(string? Write, EquatableArray<LeftOutPair> LeftOut);

    /// <summary>This project and one it references, which carry one module's name and are not taken together.</summary>
    /// <param name="Other">The referenced project.</param>
    /// <param name="Module">The name both set in <c>DDD_Module</c>.</param>
    /// <param name="Who">Which of the two declares no module.</param>
    /// <param name="Registrations">For <see cref="LeftOutSide.Both"/>, the registrations written for the other's classes nowhere.</param>
    private sealed record LeftOutPair(string Other, string Module, LeftOutSide Who, EquatableArray<string> Registrations);

    /// <summary>Which project of a <see cref="LeftOutPair"/> declares no module.</summary>
    private enum LeftOutSide
    {
        /// <summary>This one: the referenced project declares the module.</summary>
        ThisProject,

        /// <summary>The referenced one: this project declares the module.</summary>
        ThatProject,

        /// <summary>Neither, and a registration is written for the referenced project's template classes nowhere.</summary>
        Both,
    }
}
