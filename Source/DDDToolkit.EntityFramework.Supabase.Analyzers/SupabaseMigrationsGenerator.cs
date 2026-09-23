using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.EntityFramework.Supabase.Analyzers;

/// <summary>
/// Finds every design-time factory marked <c>[SupabaseMigrations]</c> that the project references, and
/// writes the list of them together with a module initializer that exports their migrations when the
/// build step asks. Only in the project that sets <c>SupabaseMigrationsExport</c>; everywhere else it
/// writes nothing.
/// <para>
/// The factories normally live in the module projects and the export is switched on in the host, so the
/// search covers the referenced assemblies, not just this one. It only opens assemblies that reference
/// the Supabase package, which is the only way one of them can carry the marker, so a host with a large
/// dependency graph pays for its modules and nothing else.
/// </para>
/// <para>
/// What it writes is plain generic code, <c>SupabaseMigrationSource.For&lt;TContext, TFactory&gt;()</c>
/// per factory. Nothing is found or created by reflection when the export runs.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SupabaseMigrationsGenerator : IIncrementalGenerator
{
    private const string ExportProperty = "build_property.SupabaseMigrationsExport";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue(ExportProperty, out var mode)
            && (string.Equals(mode?.Trim(), "Write", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode?.Trim(), "Check", StringComparison.OrdinalIgnoreCase)));

        var found = context.CompilationProvider
            .Combine(enabled)
            .Select(static (pair, cancellationToken) => pair.Right ? Discover(pair.Left, cancellationToken) : Discovery.None);

        context.RegisterSourceOutput(found, static (production, discovery) =>
        {
            discovery.Diagnostics.ReportAll(production);

            if (discovery.Enabled)
            {
                production.AddSource("DDDToolkit.SupabaseMigrationSources.g.cs", Emit(discovery.Sources));
            }
        });
    }

    private static Discovery Discover(Compilation compilation, CancellationToken cancellationToken)
    {
        var sources = new List<Source>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var assembly in Searched(compilation))
        {
            foreach (var type in TypesIn(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsMarked(type))
                {
                    continue;
                }

                if (Unusable(type, compilation) is { } reason)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.SupabaseMigrationsFactoryUnusable,
                        LocationInfo.From(type),
                        type.ToDisplayString(),
                        reason));
                    continue;
                }

                var contextType = ContextOf(type)!;
                sources.Add(new Source(
                    contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ModuleBoundary.ModuleOf(type.ContainingAssembly)));
            }
        }

        // Sorted, so the generated file does not change when the compiler happens to list references in
        // another order.
        sources.Sort(static (left, right) =>
        {
            var byModule = string.CompareOrdinal(left.Module, right.Module);
            return byModule != 0 ? byModule : string.CompareOrdinal(left.Context, right.Context);
        });

        return new Discovery(true, new EquatableArray<Source>(sources), new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    /// <summary>This assembly, and every referenced assembly that references the Supabase package.</summary>
    private static IEnumerable<IAssemblySymbol> Searched(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referenced.Modules.Any(static module => module.ReferencedAssemblies.Any(static identity =>
                    string.Equals(identity.Name, KnownTypes.SupabaseAssemblyName, StringComparison.Ordinal))))
            {
                yield return referenced;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceSymbol @namespace)
    {
        foreach (var member in @namespace.GetMembers())
        {
            if (member is INamespaceSymbol inner)
            {
                foreach (var type in TypesIn(inner))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (var nested in WithNested(type))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var inner in WithNested(nested))
            {
                yield return inner;
            }
        }
    }

    private static bool IsMarked(INamedTypeSymbol type)
        => type.GetAttributes().Any(static attribute =>
            attribute.AttributeClass is { Name: KnownTypes.SupabaseMigrationsAttributeName } attributeClass
            && attributeClass.ContainingNamespace.ToDisplayString() == KnownTypes.SupabaseNamespace);

    /// <summary>The context of the factory's <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c>, or null.</summary>
    private static INamedTypeSymbol? ContextOf(INamedTypeSymbol factory)
        => factory.AllInterfaces
            .Where(static candidate =>
                candidate.OriginalDefinition.MetadataName == "IDesignTimeDbContextFactory`1"
                && candidate.OriginalDefinition.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore.Design")
            .Select(static candidate => candidate.TypeArguments[0] as INamedTypeSymbol)
            .FirstOrDefault();

    /// <summary>
    /// Why the generated code could not create this factory, or null when it can. Generated code calls
    /// <c>For&lt;TContext, TFactory&gt;()</c>, whose <c>new()</c> constraint wants a public parameterless
    /// constructor, and it names both types, so both must be reachable from this project.
    /// </summary>
    private static string? Unusable(INamedTypeSymbol factory, Compilation compilation)
    {
        if (factory.TypeKind != TypeKind.Class || factory.IsAbstract || factory.IsStatic)
        {
            return "it is not a concrete class";
        }

        if (factory.IsGenericType)
        {
            return "it is generic, and the build would not know what to close it with";
        }

        if (ContextOf(factory) is not { } context)
        {
            return "it does not implement IDesignTimeDbContextFactory<TContext>";
        }

        if (!compilation.IsSymbolAccessibleWithin(factory, compilation.Assembly))
        {
            return $"'{compilation.AssemblyName}' cannot see it; make it public";
        }

        if (!compilation.IsSymbolAccessibleWithin(context, compilation.Assembly))
        {
            return $"'{compilation.AssemblyName}' cannot see its context '{context.ToDisplayString()}'; make that public";
        }

        if (!factory.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public))
        {
            return "it has no public parameterless constructor";
        }

        return null;
    }

    private static string Emit(EquatableArray<Source> sources)
    {
        const string Supabase = "global::" + KnownTypes.SupabaseNamespace;

        var entries = sources.Count == 0
            ? string.Empty
            : string.Join(
                "\n",
                sources.Select(source =>
                    $"            {Supabase}.SupabaseMigrationSource.For<{source.Context}, {source.Factory}>({(source.Module is null ? "null" : Literal(source.Module))}),"));

        return $$"""
            // <auto-generated/>
            #nullable enable

            namespace DDDToolkit.EntityFramework.Supabase.Generated
            {
                /// <summary>
                /// Every factory marked [SupabaseMigrations] this project references, found when it was compiled,
                /// and the hook the build's export step runs through.
                /// </summary>
                internal static class SupabaseMigrationSources
                {
                    /// <summary>The contexts whose migrations the build exports, one per marked factory.</summary>
                    public static global::System.Collections.Generic.IReadOnlyList<{{Supabase}}.SupabaseMigrationSource> All()
                        => new {{Supabase}}.SupabaseMigrationSource[]
                        {
            {{entries}}
                        };

                    /// <summary>
                    /// Runs before Main. Returns at once unless the build's export step started this process, in which
                    /// case it exports and ends the process before any of the application's own start-up runs.
                    /// </summary>
                    [global::System.Runtime.CompilerServices.ModuleInitializer]
                    internal static void ExportWhenTheBuildAsks()
                        => {{Supabase}}.SupabaseMigrationBuild.RunIfRequested(All);
                }
            }

            """;
    }

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>One marked factory: its context, itself, and the module its assembly declares, if any.</summary>
    private sealed record Source(string Context, string Factory, string? Module);

    private sealed record Discovery(bool Enabled, EquatableArray<Source> Sources, EquatableArray<DiagnosticInfo> Diagnostics)
    {
        public static readonly Discovery None = new(false, EquatableArray<Source>.Empty, EquatableArray<DiagnosticInfo>.Empty);
    }
}
