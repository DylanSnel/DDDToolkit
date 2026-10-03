using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// MSBuild properties exposed to the generators through <c>CompilerVisibleProperty</c> items, which
/// build/ and buildTransitive/&lt;package id&gt;.props inside the DDDToolkit.Analyzers package declare.
/// </summary>
/// <param name="ModuleName">The project's <c>DDD_Module</c>, or empty when it sets none.</param>
/// <param name="PropertiesDeclared">
/// Whether the properties reached the compiler at all. A property that is declared and not set arrives
/// as an empty value; one that is not declared does not arrive, and that only happens when the props
/// file was not imported. The generators then ignore whatever the project sets, which is DDD00014.
/// </param>
internal sealed record DDDOptions(string ModuleName, bool PropertiesDeclared)
{
    public static readonly DDDOptions Default = new(string.Empty, PropertiesDeclared: true);
}

internal static class DDDOptionsProvider
{
    public static IncrementalValueProvider<DDDOptions> GetDDDOptions(this IncrementalGeneratorInitializationContext context)
        => context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            var declared = provider.GlobalOptions.TryGetValue("build_property.DDD_Module", out var moduleName);

            return new DDDOptions(ModuleName: moduleName?.Trim() ?? string.Empty, PropertiesDeclared: declared);
        });

    /// <summary>
    /// The name in the generated registration methods, <c>Add{Module}Converters</c> and the others, as a
    /// cacheable value. See <see cref="ResolveModuleName(DDDOptions, string?, string?)"/> for where it
    /// comes from.
    /// </summary>
    public static IncrementalValueProvider<string> RegistrationName(this IncrementalGeneratorInitializationContext context)
        => context.GetDDDOptions()
            .Combine(context.CompilationProvider.Select(static (compilation, _) => (Module: ModuleBoundary.ModuleOf(compilation.Assembly), compilation.AssemblyName)))
            .Select(static (pair, _) => pair.Left.ResolveModuleName(pair.Right.Module, pair.Right.AssemblyName));

    /// <summary>The module name from MSBuild, or a name derived from the assembly name.</summary>
    public static string ResolveModuleName(this DDDOptions options, string? assemblyName)
        => options.ResolveModuleName(module: null, assemblyName);

    /// <summary>
    /// The name generated code is given. The module the assembly declares with <c>[assembly: Module]</c>
    /// always wins. <c>DDD_Module</c> is the default beneath it, which a <c>Directory.Build.props</c> can
    /// set for a whole folder, and the assembly name is what is left when a project has neither.
    /// </summary>
    public static string ResolveModuleName(this DDDOptions options, string? module, string? assemblyName)
    {
        // A module's name is free text, "order-management" for one; the same spelling {Module}EventNames uses.
        if (module is not null && EventNaming.Pascal(module) is { Length: > 0 } identifier)
        {
            return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
        }

        if (!string.IsNullOrEmpty(options.ModuleName))
        {
            return options.ModuleName;
        }

        return assemblyName is null ? "AssemblyTypes" : assemblyName.Replace(".", string.Empty);
    }

    /// <summary>
    /// The registration methods the other assemblies of this module generated, as calls ready to write:
    /// <c>global::Ordering.Contracts.Converters.ConverterExtensions.AddOrderingConverters</c>.
    /// <para>
    /// Two assemblies that declare the same <c>[assembly: Module]</c> are one module, and both name
    /// their registration after it. An assembly's own method therefore calls the ones of the module's
    /// assemblies it references, so one call registers the whole module and code never has to import
    /// two classes that declare the same extension method.
    /// </para>
    /// </summary>
    /// <param name="className">The generated class, under the assembly's namespace: <c>Converters.ConverterExtensions</c>.</param>
    /// <param name="methodSuffix">What the method's name ends in: <c>Converters</c>.</param>
    public static IncrementalValueProvider<EquatableArray<string>> RegistrationsOfTheSameModule(
        this IncrementalGeneratorInitializationContext context,
        string className,
        string methodSuffix)
        => context.CompilationProvider.Select((compilation, _) =>
        {
            var calls = RegistrationsOfTheSameModule(compilation, className, methodSuffix)
                .SelectMany(static registration => registration.Calls)
                .ToList();

            calls.Sort(StringComparer.Ordinal);
            return new EquatableArray<string>(calls.ToArray());
        });

    /// <summary>
    /// The assemblies of this compilation's module that it references and that generated a registration of
    /// their own, each with the calls to it. Empty when the compilation declares no module.
    /// <para>
    /// A generator that also registers types of other projects asks this before it does: what one of these
    /// assemblies registers is reached through the call to it, and is not written a second time.
    /// </para>
    /// </summary>
    /// <param name="compilation">The project the registration is written into.</param>
    /// <param name="className">The generated class, under the assembly's namespace: <c>Converters.ConverterExtensions</c>.</param>
    /// <param name="methodSuffix">What the method's name ends in: <c>Converters</c>.</param>
    public static IReadOnlyList<ModuleRegistration> RegistrationsOfTheSameModule(Compilation compilation, string className, string methodSuffix)
    {
        var module = ModuleBoundary.ModuleOf(compilation.Assembly);
        if (module is null)
        {
            return Array.Empty<ModuleRegistration>();
        }

        var registrations = new List<ModuleRegistration>();

        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!string.Equals(ModuleBoundary.ModuleOf(referenced), module, StringComparison.Ordinal))
            {
                continue;
            }

            var type = referenced.GetTypeByMetadataName(Identifiers.NamespaceFrom(referenced.Name) + "." + className);
            if (type is not { IsStatic: true, DeclaredAccessibility: Accessibility.Public })
            {
                continue;
            }

            // Found by its shape rather than by the name this assembly would give it: an assembly built
            // by an earlier version named its method after DDD_Module.
            var calls = type.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method is { IsExtensionMethod: true, DeclaredAccessibility: Accessibility.Public, Parameters.Length: 1 }
                    && method.Name.StartsWith("Add", StringComparison.Ordinal)
                    && method.Name.EndsWith(methodSuffix, StringComparison.Ordinal))
                .Select(method => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name)
                .ToArray();

            if (calls.Length > 0)
            {
                registrations.Add(new ModuleRegistration(referenced, calls));
            }
        }

        return registrations;
    }
}

/// <summary>An assembly of the module that generated a registration of its own, as the assembly that references it sees it.</summary>
/// <param name="Assembly">The referenced assembly.</param>
/// <param name="Calls">Its registration methods, fully qualified, ready to be written as calls.</param>
internal sealed record ModuleRegistration(IAssemblySymbol Assembly, IReadOnlyList<string> Calls)
{
    /// <summary>
    /// Whether this assembly references <paramref name="other"/>, which is the first thing it needs to have
    /// registered a type of it.
    /// </summary>
    public bool References(IAssemblySymbol other)
    {
        foreach (var part in Assembly.Modules)
        {
            foreach (var reference in part.ReferencedAssemblySymbols)
            {
                if (SymbolEqualityComparer.Default.Equals(reference, other))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
