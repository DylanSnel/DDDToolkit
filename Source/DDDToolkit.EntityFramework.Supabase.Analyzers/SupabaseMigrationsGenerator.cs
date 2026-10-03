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
/// And only in an application that is not a test project, the projects the build step runs in. The step
/// starts the program the project built, so a library has nothing it could start, and a test project is not
/// the host. The property may be given for a whole build, which hands it to every project; there every
/// library and test project reports nothing and writes nothing, as if it were not set. Another application
/// in that build is a host as far as anything here can tell, and exports.
/// </para>
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
/// <para>
/// It hands the export the row access contributions this project lists with
/// <c>[assembly: UseRowAccessContribution(typeof(X))]</c>, as <c>new X()</c>, and no others: SQL a package offers
/// with <c>[assembly: RowAccessContribution]</c> reaches the migrations only by the host's choice. An offer in
/// the searched assemblies that the host lists neither itself, nor as a class derived from it, nor closed with
/// its own types, is DDD00054.
/// </para>
/// <para>
/// A marked factory whose assembly and whose context's assembly both declare no <c>[assembly: Module]</c> is
/// DDD00055: its files would be named after the context's class, and a rename would orphan them all.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SupabaseMigrationsGenerator : IIncrementalGenerator
{
    private const string ExportProperty = "build_property.SupabaseMigrationsExport";

    /// <summary>What Microsoft.NET.Test.Sdk sets in a test project, and what a project may set to say it is one.</summary>
    private const string TestProjectProperty = "build_property.IsTestProject";

    /// <summary>What Microsoft.Testing.Platform sets in a test application, which may lack the other.</summary>
    private const string TestingPlatformProperty = "build_property.IsTestingPlatformApplication";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var turnedOn = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue(ExportProperty, out var mode)
            && (string.Equals(mode?.Trim(), "Write", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode?.Trim(), "Check", StringComparison.OrdinalIgnoreCase))
            && !IsSet(options.GlobalOptions, TestProjectProperty)
            && !IsSet(options.GlobalOptions, TestingPlatformProperty));

        // The compiler's own word for what the build step can start: an OutputType of Exe or WinExe.
        var application = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication);

        var enabled = turnedOn.Combine(application).Select(static (pair, _) => pair.Left && pair.Right);

        var found = context.CompilationProvider
            .Combine(enabled)
            .Select(static (pair, cancellationToken) => pair.Right ? Discover(pair.Left, cancellationToken) : Discovery.None);

        context.RegisterSourceOutput(found, static (production, discovery) =>
        {
            discovery.Diagnostics.ReportAll(production);

            if (discovery.Enabled)
            {
                production.AddSource("DDDToolkit.SupabaseMigrationSources.g.cs", Emit(discovery.Sources, discovery.Rules, discovery.Functions, discovery.Contributions));
            }
        });
    }

    private static Discovery Discover(Compilation compilation, CancellationToken cancellationToken)
    {
        var sources = new List<Source>();
        var rules = new List<Rule>();
        var functions = new List<Function>();
        var diagnostics = new List<DiagnosticInfo>();
        var used = ContributionsIn(compilation.Assembly, KnownTypes.UseRowAccessContributionAttribute).ToList();

        foreach (var assembly in Searched(compilation))
        {
            foreach (var offered in ContributionsIn(assembly, KnownTypes.RowAccessContributionAttribute))
            {
                if (!used.Any(type => Covers(type, offered)))
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.RowAccessContributionNotUsed,
                        null,
                        assembly.Name,
                        offered.OriginalDefinition.ToDisplayString(),
                        HowToList(offered.OriginalDefinition)));
                }
            }

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
                var module = ModuleBoundary.ModuleOf(type.ContainingAssembly);

                // Without a module here, the export asks the context's own assembly when it runs, and names the
                // files after the context's class when that declares none either: a name that a rename changes.
                if (module is null && ModuleBoundary.ModuleOf(contextType.ContainingAssembly) is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.SupabaseMigrationsWithoutModule,
                        LocationInfo.From(type),
                        contextType.ToDisplayString(),
                        NameInFiles(contextType.Name)));
                }

                sources.Add(new Source(
                    contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    module));
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

        var contributions = used
            .Select(static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        contributions.Sort(StringComparer.Ordinal);

        return new Discovery(
            true,
            new EquatableArray<Source>(sources),
            new EquatableArray<Rule>(rules),
            new EquatableArray<Function>(functions),
            new EquatableArray<string>(contributions),
            new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    /// <summary>Whether the build property <paramref name="key"/> is <c>true</c>, as MSBuild reads a condition: without regard to case.</summary>
    private static bool IsSet(Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions options, string key)
        => options.TryGetValue(key, out var value) && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name the export gives the files of a context whose assemblies declare no module, as the export itself
    /// works it out when it runs: the class name without a trailing <c>Context</c>, in lower case, with anything
    /// but letters and digits turned into a dash.
    /// </summary>
    private static string NameInFiles(string contextName)
    {
        const string Suffix = "Context";
        var name = contextName.EndsWith(Suffix, StringComparison.Ordinal) && contextName.Length > Suffix.Length
            ? contextName.Substring(0, contextName.Length - Suffix.Length)
            : contextName;

        var normalized = new System.Text.StringBuilder(name.Length);
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            normalized.Append((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') ? character : '-');
        }

        var result = normalized.ToString().Trim('-');
        return result.Length > 0 ? result : "module";
    }

    /// <summary>
    /// The types the assembly attributes named <paramref name="attributeName"/> of <paramref name="assembly"/>
    /// name: the contributions a project lists with <c>[assembly: UseRowAccessContribution(typeof(X))]</c>, or
    /// those an assembly offers with <c>[assembly: RowAccessContribution(typeof(X))]</c>.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ContributionsIn(IAssemblySymbol assembly, string attributeName)
        => assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName)
            .Select(static attribute => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol type ? type : null)
            .Where(static type => type is not null)
            .Select(static type => type!)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    /// <summary>
    /// What DDD00054 tells the host to write for an offer it does not use, as code that compiles once the host's
    /// own types are put in. The export creates every contribution it is handed with <c>new X()</c>, in the
    /// generated list, so what is listed closes every type parameter and takes nothing. An offer that does both
    /// is listed as it is. A generic one that takes nothing is closed in the attribute. One whose constructor
    /// takes an argument, the rules of the application's resource say, is listed through a class of the host's
    /// that derives from it and hands that over; a type parameter of such a class is closed in that line.
    /// </summary>
    private static string HowToList(INamedTypeSymbol offered)
    {
        var takesNothing = offered.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public);
        var closedWithYours = offered.TypeParameters.Length == 0
            ? offered.Name
            : offered.Name + "<" + string.Join(", ", offered.TypeParameters.Select(static parameter => "Your" + Unprefixed(parameter.Name))) + ">";
        var forEach = offered.TypeParameters.Length == 0
            ? string.Empty
            : ", with a type of yours for " + string.Join(" and ", offered.TypeParameters.Select(static parameter => parameter.Name));
        var qualified = (offered.ContainingType is { } outer ? outer.ToDisplayString() + "."
                            : offered.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() + "."
                            : string.Empty) + closedWithYours;

        if (takesNothing && offered.TypeParameters.Length == 0)
        {
            return "Add [assembly: UseRowAccessContribution(typeof(" + offered.ToDisplayString() + "))], or list a class of yours that derives from it";
        }

        if (takesNothing)
        {
            return "Add [assembly: UseRowAccessContribution(typeof(" + qualified + "))]" + forEach + ", or list a class of yours that derives from it";
        }

        return "Its constructor takes what only your application can give it, so declare a class of yours that derives from it and hands that over, "
               + "public sealed class YourRowAccess() : " + qualified + "(...)" + forEach + ", and add [assembly: UseRowAccessContribution(typeof(YourRowAccess))]";
    }

    /// <summary>A type parameter's name without its leading <c>T</c>: <c>Member</c> of <c>TMember</c>, and a name that has none as it is.</summary>
    private static string Unprefixed(string name)
        => name.Length > 1 && name[0] == 'T' && char.IsUpper(name[1]) ? name.Substring(1) : name;

    /// <summary>
    /// Whether the host's <paramref name="used"/> contribution is the <paramref name="offered"/> one: the same
    /// class, a class of the host's derived from it, or, for a generic one, the host's own closing of it. A
    /// package whose SQL depends on what only the host knows, such as its catalogue, offers a class the host
    /// derives from or closes with its own types.
    /// </summary>
    private static bool Covers(INamedTypeSymbol used, INamedTypeSymbol offered)
    {
        for (INamedTypeSymbol? type = used; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, offered)
                || (offered.IsGenericType && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, offered.OriginalDefinition)))
            {
                return true;
            }
        }

        return false;
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
    /// <c>RowAccessSql</c>, or null, for the same reason as <see cref="RuleOf"/>. Its name is the one the core
    /// generator wrote as <c>Name</c>, which is the logical <c>owner/name</c> for a name without a schema, and
    /// with it come the SQL types of the parameters it takes after the key and its shape.
    /// </summary>
    private static Function? FunctionOf(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(static candidate =>
            candidate.AttributeClass?.OriginalDefinition is { MetadataName: "AccessFunctionAttribute`1" } function
            && function.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

        if (attribute?.AttributeClass?.TypeArguments.FirstOrDefault() is not INamedTypeSymbol aggregate
            || attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not string written
            || Constant(type, KnownTypes.RowAccessSqlField) is not string sql)
        {
            return null;
        }

        var name = Constant(type, "Name") as string ?? written;
        var parameters = Constant(type, "RowAccessParameters") as string ?? "";
        var shape = Constant(type, "RowAccessShape") is int value ? value : 0;
        return new Function(ClrName(aggregate), name, sql, parameters, shape);
    }

    /// <summary>The value of the constant <paramref name="name"/> the core generator wrote into <paramref name="type"/>, or null.</summary>
    private static object? Constant(INamedTypeSymbol type, string name)
        => type.GetMembers(name).OfType<IFieldSymbol>().FirstOrDefault() is { HasConstantValue: true } field ? field.ConstantValue : null;

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

    private static string Emit(EquatableArray<Source> sources, EquatableArray<Rule> rules, EquatableArray<Function> functions, EquatableArray<string> contributions)
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
                    $"            {Postgres}.RowAccessFunction.For({Literal(function.Aggregate)}, {Literal(function.Name)}, {Literal(function.Sql)}"
                    + (function.Parameters.Length == 0 && function.Shape == 0
                        ? ""
                        : $", owner: null, parameters: {Literal(function.Parameters)}, shape: (global::{KnownTypes.AttributesNamespace}.AccessFunctionShape){function.Shape}")
                    + "),"));

        var contributionEntries = contributions.Count == 0
            ? string.Empty
            : string.Join("\n", contributions.Select(contribution => $"            new {contribution}(),"));

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
                    /// The row access contributions this project lists with [assembly: UseRowAccessContribution], which the
                    /// export asks what they write for each context.
                    /// </summary>
                    public static global::System.Collections.Generic.IReadOnlyList<{{Postgres}}.IRowAccessContribution> Contributions()
                        => new {{Postgres}}.IRowAccessContribution[]
                        {
            {{contributionEntries}}
                        };

                    /// <summary>
                    /// Runs before Main. Returns at once unless the build's export step started this process, in which
                    /// case it exports and ends the process before any of the application's own start-up runs.
                    /// </summary>
                    [global::System.Runtime.CompilerServices.ModuleInitializer]
                    internal static void ExportWhenTheBuildAsks()
                        => {{Supabase}}.SupabaseMigrationBuild.RunIfRequested(All, Rules, Functions, Contributions);
                }
            }

            """
            // The literal has the line endings this file was checked out with, which is CRLF on Windows with
            // core.autocrlf, while the entries are joined with "\n". Everything is written with "\n", as the
            // other generators do, so the generated file is the same whichever machine built the generator.
            .Replace("\r\n", "\n");
    }

    private static string Literal(string value)
        => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>One marked factory: its context, itself, and the module its assembly declares, if any.</summary>
    private sealed record Source(string Context, string Factory, string? Module);

    /// <summary>One row access rule: the aggregate's CLR name, the policy's name, what it allows, for whom, and its SQL.</summary>
    private sealed record Rule(string Aggregate, string Name, int Operations, EquatableArray<string> Roles, string Sql);

    /// <summary>
    /// One access function: the aggregate's CLR name, the function's name, its SQL, the SQL types of the
    /// parameters it takes after the key, and its shape, as <c>AccessFunctionShape</c> numbers it.
    /// </summary>
    private sealed record Function(string Aggregate, string Name, string Sql, string Parameters, int Shape);

    /// <summary>
    /// What the generator found: the sources, rules and access functions to export, the contributions the host
    /// uses, by their fully qualified names, and the diagnostics to report.
    /// </summary>
    private sealed record Discovery(
        bool Enabled,
        EquatableArray<Source> Sources,
        EquatableArray<Rule> Rules,
        EquatableArray<Function> Functions,
        EquatableArray<string> Contributions,
        EquatableArray<DiagnosticInfo> Diagnostics)
    {
        public static readonly Discovery None = new(false, EquatableArray<Source>.Empty, EquatableArray<Rule>.Empty, EquatableArray<Function>.Empty, EquatableArray<string>.Empty, EquatableArray<DiagnosticInfo>.Empty);
    }
}
