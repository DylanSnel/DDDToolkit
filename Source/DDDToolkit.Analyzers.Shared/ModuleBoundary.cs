using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

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
/// </summary>
internal static class ModuleBoundary
{
    /// <summary>File name suffixes Roslyn treats as generated code, matched the same way here.</summary>
    private static readonly string[] GeneratedFileSuffixes =
    [
        ".g.cs",
        ".g.i.cs",
        ".generated.cs",
        ".designer.cs",
    ];

    /// <summary>The name of the module this assembly declares, or null when it declares none.</summary>
    public static string? ModuleOf(IAssemblySymbol? assembly)
    {
        if (assembly is null)
        {
            return null;
        }

        foreach (var attribute in assembly.GetAttributes())
        {
            if (!IsToolkitAttribute(attribute, KnownTypes.ModuleAttribute))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string name
                && !string.IsNullOrWhiteSpace(name))
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

    /// <summary>Whether the type carries <c>[Entity&lt;T&gt;]</c> or <c>[AggregateRoot&lt;T&gt;]</c>.</summary>
    public static bool IsEntity(INamedTypeSymbol type)
        => DefinitionFactory.HasAttribute(type, KnownTypes.EntityAttribute)
           || DefinitionFactory.HasAttribute(type, KnownTypes.AggregateRootAttribute);

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
