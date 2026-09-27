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
                production.AddSource("DDDToolkit.SupabaseMigrationSources.g.cs", Emit(discovery.Sources, discovery.Rules, discovery.Functions));
            }
        });
    }

    private static Discovery Discover(Compilation compilation, CancellationToken cancellationToken)
    {
        var sources = new List<Source>();
        var rules = new List<Rule>();
        var functions = new List<Function>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var assembly in Searched(compilation))
        {
            foreach (var type in TypesIn(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (FunctionOf(type) is { } function)
                {
                    functions.Add(function);
                    continue;
                }

                if (RuleOf(type) is { } rule)
                {
                    rules.Add(rule);
                    continue;
                }

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

        rules.Sort(static (left, right) =>
        {
            var byAggregate = string.CompareOrdinal(left.Aggregate, right.Aggregate);
            return byAggregate != 0 ? byAggregate : string.CompareOrdinal(left.Name, right.Name);
        });

        functions.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return new Discovery(true, new EquatableArray<Source>(sources), new EquatableArray<Rule>(rules), new EquatableArray<Function>(functions), new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    /// <summary>
    /// This assembly, and every referenced assembly that references the Supabase package, where marked
    /// factories live, or the toolkit's abstractions, where row access rules live.
    /// </summary>
    private static IEnumerable<IAssemblySymbol> Searched(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referenced.Modules.Any(static module => module.ReferencedAssemblies.Any(static identity =>
                    string.Equals(identity.Name, KnownTypes.SupabaseAssemblyName, StringComparison.Ordinal)
                    || string.Equals(identity.Name, AbstractionsAssemblyName, StringComparison.Ordinal))))
            {
                yield return referenced;
            }
        }
    }

    private const string AbstractionsAssemblyName = "DDDToolkit.Abstractions";

    /// <summary>
    /// An <c>[AccessFunction&lt;TAggregate&gt;]</c> whose SQL the core generator wrote into it as
    /// <c>RowAccessSql</c>, or null, for the same reason as <see cref="RuleOf"/>.
    /// </summary>
    private static Function? FunctionOf(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(static candidate =>
            candidate.AttributeClass?.OriginalDefinition is { MetadataName: "AccessFunctionAttribute`1" } function
            && function.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

        return attribute?.AttributeClass?.TypeArguments.FirstOrDefault() is INamedTypeSymbol aggregate
            && attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string name
            && type.GetMembers(KnownTypes.RowAccessSqlField).OfType<IFieldSymbol>().FirstOrDefault() is { HasConstantValue: true, ConstantValue: string sql }
                ? new Function(ClrName(aggregate), name, sql)
                : null;
    }

    /// <summary>
    /// A <c>[RowAccess&lt;TAggregate&gt;]</c> rule whose SQL the core generator wrote into it as the constant
    /// <c>RowAccessSql</c>, or null. A rule without the constant did not translate, and its own project
    /// already failed to build with the reason, so it is left out here.
    /// </summary>
    private static Rule? RuleOf(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(static candidate =>
            candidate.AttributeClass?.OriginalDefinition is { MetadataName: "RowAccessAttribute`1" } rowAccess
            && rowAccess.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

        if (attribute?.AttributeClass?.TypeArguments.FirstOrDefault() is not INamedTypeSymbol aggregate
            || type.GetMembers(KnownTypes.RowAccessSqlField).OfType<IFieldSymbol>().FirstOrDefault() is not { HasConstantValue: true, ConstantValue: string sql })
        {
            return null;
        }

        var operations = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int value ? value : 0;
        var roles = attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "To").Value is { Kind: TypedConstantKind.Array } to
            ? to.Values.Select(static role => role.Value as string).Where(static role => !string.IsNullOrWhiteSpace(role)).Select(static role => role!).ToArray()
            : [];

        return new Rule(ClrName(aggregate), Humanize(type.Name), operations, new EquatableArray<string>(roles), sql);
    }

    /// <summary>The name <see cref="Type.FullName"/> gives the type: namespace, then outer types with <c>+</c>.</summary>
    private static string ClrName(INamedTypeSymbol type)
    {
        var name = type.MetadataName;
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            name = outer.MetadataName + "+" + name;
        }

        return type.ContainingNamespace.IsGlobalNamespace ? name : type.ContainingNamespace.ToDisplayString() + "." + name;
    }

    /// <summary><c>ACustomerSeesTheirOrders</c> as <c>A customer sees their orders</c>, the name Postgres shows.</summary>
    private static string Humanize(string name)
    {
        var words = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            var startsWord = i > 0 && (char.IsUpper(character) || (char.IsDigit(character) && !char.IsDigit(name[i - 1])));
            if (startsWord)
            {
                words.Append(' ');
            }

            words.Append(i > 0 && char.IsUpper(character) ? char.ToLowerInvariant(character) : character);
        }

        return words.ToString();
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

    private static string Emit(EquatableArray<Source> sources, EquatableArray<Rule> rules, EquatableArray<Function> functions)
    {
        const string Supabase = "global::" + KnownTypes.SupabaseNamespace;
        const string Postgres = "global::" + KnownTypes.PostgresNamespace;

        var entries = sources.Count == 0
            ? string.Empty
            : string.Join(
                "\n",
                sources.Select(source =>
                    $"            {Supabase}.SupabaseMigrationSource.For<{source.Context}, {source.Factory}>({(source.Module is null ? "null" : Literal(source.Module))}),"));

        var ruleEntries = rules.Count == 0
            ? string.Empty
            : string.Join(
                "\n",
                rules.Select(rule =>
                    $"            {Postgres}.RowAccessRule.For({Literal(rule.Aggregate)}, {Literal(rule.Name)}, (global::{KnownTypes.AttributesNamespace}.RowOperations){rule.Operations}, {Literal(rule.Sql)}"
                    + string.Concat(rule.Roles.Select(role => ", " + Literal(role))) + "),"));

        var functionEntries = functions.Count == 0
            ? string.Empty
            : string.Join(
                "\n",
                functions.Select(function =>
                    $"            {Postgres}.RowAccessFunction.For({Literal(function.Aggregate)}, {Literal(function.Name)}, {Literal(function.Sql)}),"));

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

                    /// <summary>The [RowAccess] rules of the modules, which the export writes as policies.</summary>
                    public static global::System.Collections.Generic.IReadOnlyList<{{Postgres}}.RowAccessRule> Rules()
                        => new {{Postgres}}.RowAccessRule[]
                        {
            {{ruleEntries}}
                        };

                    /// <summary>The [AccessFunction]s of the modules, which the rules call and the export writes as functions.</summary>
                    public static global::System.Collections.Generic.IReadOnlyList<{{Postgres}}.RowAccessFunction> Functions()
                        => new {{Postgres}}.RowAccessFunction[]
                        {
            {{functionEntries}}
                        };

                    /// <summary>
                    /// Runs before Main. Returns at once unless the build's export step started this process, in which
                    /// case it exports and ends the process before any of the application's own start-up runs.
                    /// </summary>
                    [global::System.Runtime.CompilerServices.ModuleInitializer]
                    internal static void ExportWhenTheBuildAsks()
                        => {{Supabase}}.SupabaseMigrationBuild.RunIfRequested(All, Rules, Functions);
                }
            }

            """;
    }

    private static string Literal(string value)
        => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>One marked factory: its context, itself, and the module its assembly declares, if any.</summary>
    private sealed record Source(string Context, string Factory, string? Module);

    /// <summary>One row access rule: the aggregate's CLR name, the policy's name, what it allows, for whom, and its SQL.</summary>
    private sealed record Rule(string Aggregate, string Name, int Operations, EquatableArray<string> Roles, string Sql);

    /// <summary>One access function: the aggregate's CLR name, the function's name, and its SQL.</summary>
    private sealed record Function(string Aggregate, string Name, string Sql);

    private sealed record Discovery(bool Enabled, EquatableArray<Source> Sources, EquatableArray<Rule> Rules, EquatableArray<Function> Functions, EquatableArray<DiagnosticInfo> Diagnostics)
    {
        public static readonly Discovery None = new(false, EquatableArray<Source>.Empty, EquatableArray<Rule>.Empty, EquatableArray<Function>.Empty, EquatableArray<DiagnosticInfo>.Empty);
    }
}
