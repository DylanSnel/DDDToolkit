using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// The module rules behind <see cref="DiagnosticDescriptors.TypeIsNotPublishedByItsModule"/> (DDD00022)
/// and <see cref="DiagnosticDescriptors.DoNotHoldAnotherModulesEntity"/> (DDD00023).
/// <para>
/// A module is an assembly carrying <c>[assembly: Module("Name")]</c>. Two assemblies that declare the
/// same name are one module, which is what lets a module be split over a domain project and an
/// infrastructure project. An assembly without the attribute is not a module and is never reported
/// against: the framework, NuGet packages and a shared kernel stay out of the way.
/// </para>
/// <para>
/// This is deliberately assembly scoped. The analyzer reads the module of a referenced assembly out of
/// its metadata, and an assembly attribute is the only declaration that survives that trip. A namespace
/// cannot carry an attribute, so several modules inside one project cannot be described this way and
/// are not supported.
/// </para>
/// <para>
/// The build declares the module as well: a project that sets <c>DDD_Module</c> is compiled with it written into it
/// as <c>AssemblyMetadata</c>, by the targets of the DDDToolkit.Analyzers package, and is that module's project as if
/// it declared <c>[assembly: Module]</c>. A test project, and one that sets <c>DDD_DeclareModule</c> to false, get
/// nothing written and are no module. The attribute still decides wherever there is one. See <see cref="ModuleOf"/>.
/// </para>
/// </summary>
internal static class ModuleBoundary
{
    /// <summary>
    /// The <c>AssemblyMetadata</c> key the build declares a project's module under: its <c>DDD_Module</c>, written only
    /// into a project the property makes a module.
    /// </summary>
    public const string ModuleNameMetadata = "DDD_Module";

    /// <summary>File name suffixes Roslyn treats as generated code, matched the same way here.</summary>
    private static readonly string[] GeneratedFileSuffixes =
    [
        ".g.cs",
        ".g.i.cs",
        ".generated.cs",
        ".designer.cs",
    ];

    /// <summary>
    /// The name of the module this assembly declares, or null when it declares none.
    /// <para>
    /// <c>[assembly: Module]</c> decides, wherever it is written: in a file of the project, or in the
    /// <c>AssemblyInfo.cs</c> an <c>AssemblyAttribute</c> item gives it. Without one, the module the build declared:
    /// the project's <c>DDD_Module</c>. The build writes it into the project for every generator of it to read here,
    /// because a generator never sees what another one writes: the <c>[assembly: Module]</c> the toolkit's generator
    /// writes from it comes too late for the others. That attribute is for what reads the compiled assembly: the
    /// analyzers, the runtime and reflection.
    /// </para>
    /// <para>
    /// An assembly whose <c>[assembly: Module]</c> names no module declares none, and does not fall back on the
    /// build's either: the attribute is there, so nothing writes a second one, and both answers stay the same.
    /// </para>
    /// </summary>
    public static string? ModuleOf(IAssemblySymbol? assembly)
    {
        if (assembly is null)
        {
            return null;
        }

        var attributes = assembly.GetAttributes();
        var declared = false;
        foreach (var attribute in attributes)
        {
            if (!IsToolkitAttribute(attribute, KnownTypes.ModuleAttribute))
            {
                continue;
            }

            declared = true;
            if (attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string name
                && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return declared ? null : BuildModuleOf(attributes);
    }

    /// <summary>
    /// Whether the assembly carries <c>[assembly: Module]</c> at all, whatever it names. Where it does, the build's
    /// declaration is not read, and the toolkit's generator writes no attribute of its own.
    /// </summary>
    public static bool HasModuleAttribute(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (IsToolkitAttribute(attribute, KnownTypes.ModuleAttribute))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The module the build declared in the assembly: the project's <c>DDD_Module</c>, as the targets wrote it. Null
    /// when the project sets no <c>DDD_Module</c>, is a test project, sets <c>DDD_DeclareModule</c> to false, or was
    /// built without the toolkit's targets, as an earlier version of the toolkit built every project.
    /// </summary>
    public static string? BuildModuleOf(IAssemblySymbol assembly) => BuildModuleOf(assembly.GetAttributes());

    private static string? BuildModuleOf(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is { Name: "AssemblyMetadataAttribute" } attributeClass
                && attribute.ConstructorArguments.Length == 2
                && attribute.ConstructorArguments[0].Value is ModuleNameMetadata
                && attribute.ConstructorArguments[1].Value is string name
                && !string.IsNullOrWhiteSpace(name)
                && attributeClass.ContainingNamespace.ToDisplayString() == "System.Reflection")
            {
                return name.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Which module each assembly of one compilation belongs to, answered once instead of once per name
    /// in the source. Built at compilation start and never written to again, so the analyzer can read it
    /// from every thread.
    /// </summary>
    public sealed class Map
    {
        private readonly Dictionary<ISymbol, string?> _modules;

        private Map(string module, Dictionary<ISymbol, string?> modules)
        {
            Module = module;
            _modules = modules;
        }

        /// <summary>The module the compilation being analyzed belongs to.</summary>
        public string Module { get; }

        /// <summary>
        /// The map for this compilation, or null when the compilation is not a module and there is
        /// nothing to check. Returning null is how the analyzer stays free in projects that never opted in.
        /// </summary>
        public static Map? For(Compilation compilation)
        {
            var module = ModuleOf(compilation.Assembly);
            if (module is null)
            {
                return null;
            }

            var modules = new Dictionary<ISymbol, string?>(SymbolEqualityComparer.Default)
            {
                [compilation.Assembly] = module,
            };

            foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                modules[referenced] = ModuleOf(referenced);
            }

            return new Map(module, modules);
        }

        /// <summary>
        /// The module <paramref name="type"/> belongs to when that is not the module being analyzed, and
        /// null otherwise. A type parameter, an error type and anything from an assembly that declares no
        /// module all answer null, which is what keeps the framework and NuGet packages out of the way.
        /// </summary>
        public string? OtherModuleOf(ITypeSymbol type)
        {
            var assembly = type.ContainingAssembly;
            if (assembly is null)
            {
                return null;
            }

            // The fallback is for an assembly that is not a direct reference of this compilation. It
            // recomputes rather than caching, because the map is read from every analysis thread.
            var owner = _modules.TryGetValue(assembly, out var known) ? known : ModuleOf(assembly);

            return owner is null || string.Equals(owner, Module, StringComparison.Ordinal) ? null : owner;
        }
    }

    /// <summary>
    /// Whether the type is part of its module's published contract: it carries <c>[ModuleContract]</c> or
    /// <c>[IntegrationEvent]</c>, or it is nested inside a type that does.
    /// </summary>
    public static bool IsPublished(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (DefinitionFactory.HasAttribute(current, KnownTypes.ModuleContractAttribute)
                || DefinitionFactory.HasAttribute(current, KnownTypes.IntegrationEventAttribute))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the type is declared an entity or an aggregate root, in any of the ways <see cref="EntityDeclarations"/> knows.</summary>
    public static bool IsEntity(INamedTypeSymbol type)
        => EntityDeclarations.IsEntityOrAggregateRoot(type);

    /// <summary>
    /// The first location of a symbol in code somebody wrote, or null when it is only declared in
    /// generated code. The generated partial part of an entity declares the backing field of every
    /// collection property the author wrote, and a warning on a file nobody edits helps nobody.
    /// </summary>
    public static Location? AuthoredLocationOf(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.SourceTree is { } tree && !IsGenerated(tree.FilePath))
            {
                return location;
            }
        }

        return null;
    }

    /// <summary>
    /// Where a diagnostic about the project's module points: the project's <c>[assembly: Module]</c>, in a file somebody
    /// edits, or else <paramref name="projectFile"/>. A module the build declared has no attribute in the source, and
    /// one an <c>AssemblyAttribute</c> item declares is in the <c>AssemblyInfo.cs</c> the build writes into
    /// <c>obj/</c>, which nobody edits: either way the project file is where the module is declared.
    /// </summary>
    /// <param name="compilation">The project.</param>
    /// <param name="projectFile">The project file, from <c>MSBuildProjectFullPath</c>, or null when it is not known.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    public static LocationInfo? WhereTheModuleIsDeclared(Compilation compilation, LocationInfo? projectFile, CancellationToken cancellationToken)
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (IsToolkitAttribute(attribute, KnownTypes.ModuleAttribute)
                && attribute.ApplicationSyntaxReference is { } reference
                && !IsGenerated(reference.SyntaxTree, cancellationToken))
            {
                return LocationInfo.From(reference.GetSyntax(cancellationToken));
            }
        }

        return projectFile;
    }

    /// <summary>
    /// Whether a file is generated code as Roslyn tells it: by its name, or by an <c>&lt;auto-generated&gt;</c>
    /// comment at its top, which the <c>AssemblyInfo.cs</c> the SDK writes has.
    /// </summary>
    public static bool IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
    {
        if (IsGenerated(tree.FilePath))
        {
            return true;
        }

        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                var text = trivia.ToString();
                if (text.IndexOf("<auto-generated", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("<autogenerated", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a file path is one Roslyn treats as generated code.</summary>
    public static bool IsGenerated(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var suffix in GeneratedFileSuffixes)
        {
            if (path!.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matched by name and namespace rather than by symbol identity, like everything else the generators
    /// look for, so the rule still works when the abstractions come from a different package version.
    /// </summary>
    private static bool IsToolkitAttribute(AttributeData attribute, string metadataName)
        => attribute.AttributeClass is { } attributeClass
           && attributeClass.Name == metadataName.Substring(metadataName.LastIndexOf('.') + 1)
           && attributeClass.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace;
}
