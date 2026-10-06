using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.EntityFramework.Supabase.Analyzers;

/// <summary>
/// Finds every context marked <c>[SupabaseMigrations]</c> that the project references, with the design-time factory
/// that makes it, and every design-time factory marked so itself, as before contexts could be. Into every application
/// that is not a test project, the program the modules are composed in, it writes the list of them with the one call
/// that registers them all for the start-up check, <c>services.AddSupabaseMigrations()</c>. Into the application that
/// sets <c>SupabaseMigrationsExport</c> it also writes the module initializer that exports their migrations when the
/// build step asks.
/// <para>
/// Only in an application that is not a test project, the projects the build step runs in and the one a host is.
/// The step starts the program the project built, so a library has nothing it could start, and a test project is not
/// the host. The property may be given for a whole build, which hands it to every project; there every library and
/// test project reports nothing and writes nothing, as if it were not set. Another application in that build is a
/// host as far as anything here can tell, and exports.
/// </para>
/// <para>
/// The contexts normally live in the module projects and the host composes them, so the search covers the referenced
/// assemblies, not just this one. It only opens assemblies that reference the Supabase package, which is the only way
/// one of them can carry the marker, so a host with a large dependency graph pays for its modules and nothing else.
/// A marked context is made by the factory <c>dotnet ef</c> takes when this application is its startup project, which
/// looks there first and then in the context's assembly: a factory of this application's own for the context, or else
/// the one beside the context, which <see cref="SupabaseDesignTimeFactoryGenerator"/> wrote there unless the context's
/// project has one of its own. A marked factory says itself which context it makes, and wins over both.
/// </para>
/// <para>
/// The registration covers the marked contexts the application references, through every project it references,
/// whether its container registers them or not; the check names a context it cannot resolve. A host that leaves one
/// out registers the ones it uses one by one, and a host with an <c>AddSupabaseMigrations(this IServiceCollection)</c>
/// of its own keeps it: the registration then writes the list alone.
/// </para>
/// <para>
/// What it writes is plain generic code, <c>SupabaseMigrationSource.For&lt;TContext, TFactory&gt;()</c>
/// per context. Nothing is found or created by reflection when the export or the check runs.
/// </para>
/// <para>
/// It hands the export the row access contributions of every package in the searched assemblies that declares
/// itself a contributor with <c>[assembly: RowAccessContribution]</c>: referencing the package is the consent.
/// Each is made in a class written for it into <c>DDDToolkit.RowAccessContributionsOfPackages.g.cs</c>, from the
/// static members the application marks with the attributes the package's constructor names with
/// <c>[FromApplication]</c>, unless the project leaves it out with <c>[assembly: LeaveOutRowAccessContribution]</c>.
/// The class implements <c>IPackageRowAccessContribution</c>, so the comment above what it writes names the
/// package's class and assembly, without a version: neither a release of the application nor one of the package
/// that writes the same SQL makes an access file stale. Next to them come the application's own, which the
/// project lists with <c>[assembly: UseRowAccessContribution(typeof(X))]</c>, as <c>new X()</c>. An assembly that
/// declares a module and <c>[assembly: RowAccessContribution]</c> offers its own SQL rather than writing it: one the
/// project does not list is DDD00069. A package's contribution that misses what it cannot do without is DDD00054,
/// one whose marks cannot be used DDD00072, one the project lists again, itself or a class derived from it,
/// DDD00073, and a line that leaves nothing out DDD00074.
/// </para>
/// <para>
/// A marked context or factory whose assembly and whose context's assembly both declare no <c>[assembly: Module]</c>
/// is DDD00055: its files would be named after the context's class, and a rename would orphan them all. What it
/// reports, it reports where the export is turned on, which is where the files are written; the registration of an
/// application that does not export leaves out in silence what it cannot name, as the exporting project reports it.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class SupabaseMigrationsGenerator : IIncrementalGenerator
{
    private const string ExportProperty = "build_property.SupabaseMigrationsExport";

    /// <summary>What Microsoft.NET.Test.Sdk sets in a test project, and what a project may set to say it is one.</summary>
    private const string TestProjectProperty = "build_property.IsTestProject";

    /// <summary>What Microsoft.Testing.Platform sets in a test application, which may lack the other.</summary>
    private const string TestingPlatformProperty = "build_property.IsTestingPlatformApplication";

    /// <summary>The class written into every application, with the list of the contexts and the call that registers them.</summary>
    public const string RegistrationClass = "SupabaseMigrationsOfModules";

    /// <summary>What a project is to this generator: nothing to write, the registration alone, or that and the export.</summary>
    private enum Role
    {
        None,
        Registers,
        Exports,
    }

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Whether the project asks for the export, and whether it is a test project, which neither exports nor is a host.
        var settings = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            (Export: options.GlobalOptions.TryGetValue(ExportProperty, out var mode)
                     && (string.Equals(mode?.Trim(), "Write", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(mode?.Trim(), "Check", StringComparison.OrdinalIgnoreCase)),
             Test: IsSet(options.GlobalOptions, TestProjectProperty) || IsSet(options.GlobalOptions, TestingPlatformProperty)));

        // The compiler's own word for what the build step can start, and what a host is: an OutputType of Exe or
        // WinExe. Where the package itself is not referenced, nothing written here would compile.
        var application = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication
            && compilation.GetTypeByMetadataName(KnownTypes.SupabaseNamespace + ".SupabaseMigrationSource") is not null);

        var role = settings.Combine(application).Select(static (pair, _) =>
            !pair.Right || pair.Left.Test ? Role.None
            : pair.Left.Export ? Role.Exports
            : Role.Registers);

        var found = context.CompilationProvider
            .Combine(role)
            .Select(static (pair, cancellationToken) => pair.Right == Role.None ? Discovery.None : Discover(pair.Left, pair.Right, cancellationToken));

        context.RegisterSourceOutput(found, static (production, discovery) =>
        {
            discovery.Diagnostics.ReportAll(production);

            if (discovery.Role == Role.None)
            {
                return;
            }

            production.AddSource(RegistrationClass + ".g.cs", EmitRegistration(discovery.Namespace, discovery.Sources, discovery.HostRegisters));

            if (discovery.Role == Role.Exports)
            {
                production.AddSource("DDDToolkit.SupabaseMigrationSources.g.cs", Emit(discovery.Sources, discovery.Rules, discovery.Functions, discovery.Contributions));

                // A file of its own, named for what it holds, and only where a package contributes: what a package
                // reference brings into the migrations is something a developer looks for by name.
                if (discovery.Contributions.Made.Count > 0)
                {
                    production.AddSource(PackagesFileName, EmitPackages(discovery.Contributions.Made));
                }
            }
        });
    }

    private static Discovery Discover(Compilation compilation, Role role, CancellationToken cancellationToken)
    {
        var exports = role == Role.Exports;
        var sources = new List<Source>();
        var rules = new List<Rule>();
        var functions = new List<Function>();
        var diagnostics = new List<DiagnosticInfo>();
        var searched = Searched(compilation, rules: exports).ToList();

        // The row access contributions only matter where the migrations are exported: the packages' that come with
        // their reference, and the application's own that the project lists.
        var contributions = exports ? ContributionsOf(compilation, searched, diagnostics, cancellationToken) : ContributionsToExport.None;
        var markedFactories = new List<INamedTypeSymbol>();
        var markedContexts = new List<INamedTypeSymbol>();

        // Every class dotnet ef could make a context through, under the assembly it is in: a marked context is made by
        // one in this application or else in its own assembly, as Entity Framework looks for it there.
        var factories = new List<INamedTypeSymbol>();

        foreach (var assembly in searched)
        {
            foreach (var type in TypesIn(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (exports && FunctionOf(type) is { } function)
                {
                    functions.Add(function);
                    continue;
                }

                if (exports && RuleOf(type) is { } rule)
                {
                    rules.Add(rule);
                    continue;
                }

                if (DesignTimeFactories.IsUsableByEntityFramework(type) && DesignTimeFactories.ContextsMadeBy(type).Any())
                {
                    factories.Add(type);
                }

                if (IsMarked(type))
                {
                    (DesignTimeFactories.IsContext(type) ? markedContexts : markedFactories).Add(type);
                }
            }
        }

        // A marked factory says itself which context it makes, so its context is not looked at again.
        var made = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var factory in markedFactories)
        {
            if (Unusable(factory, compilation) is { } reason)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.SupabaseMigrationsFactoryUnusable,
                    LocationInfo.From(factory),
                    factory.ToDisplayString(),
                    reason));
                continue;
            }

            var contextType = ContextOf(factory)!;
            made.Add(contextType);
            sources.Add(SourceOf(contextType, factory.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), factory, diagnostics));
        }

        foreach (var contextType in markedContexts)
        {
            if (!made.Add(contextType))
            {
                continue;
            }

            if (FactoryOf(contextType, factories, compilation, out var reason) is { } factory)
            {
                sources.Add(SourceOf(contextType, factory, contextType, diagnostics));
            }
            else if (reason is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.SupabaseMigrationsFactoryUnusable,
                    LocationInfo.From(contextType),
                    contextType.ToDisplayString(),
                    reason));
            }
        }

        // What is reported is said where the files are written: in an application that only registers, the project that
        // exports says it, and the check leaves out what it could not name.
        if (!exports)
        {
            diagnostics.Clear();
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

        return new Discovery(
            role,
            Identifiers.NamespaceFrom(compilation.AssemblyName),
            DeclaresItsOwnRegistration(compilation),
            new EquatableArray<Source>(sources),
            new EquatableArray<Rule>(rules),
            new EquatableArray<Function>(functions),
            contributions,
            new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    /// <summary>
    /// The source of <paramref name="contextType"/>, made by <paramref name="factory"/>, named after the module the
    /// factory's assembly declares; DDD00055 at <paramref name="marked"/> when neither that assembly nor the context's
    /// declares one.
    /// </summary>
    private static Source SourceOf(INamedTypeSymbol contextType, string factory, INamedTypeSymbol marked, List<DiagnosticInfo> diagnostics)
    {
        var module = ModuleBoundary.ModuleOf(marked.ContainingAssembly);

        // Without a module here, the export asks the context's own assembly when it runs, and names the files after
        // the context's class when that declares none either: a name that a rename changes.
        if (module is null && ModuleBoundary.ModuleOf(contextType.ContainingAssembly) is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.SupabaseMigrationsWithoutModule,
                LocationInfo.From(marked),
                contextType.ToDisplayString(),
                NameInFiles(contextType.Name)));
        }

        return new Source(contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), factory, module);
    }

    /// <summary>
    /// The factory a marked context is made by, as generated code names it, or null with the reason DDD00031 gives, or
    /// with none when the reason is reported elsewhere. The one <c>dotnet ef</c> takes with this application as its
    /// startup project, which looks there first and then in the context's own assembly: a factory of this
    /// application's own, which wins as a host's declaration does, or else the one beside the context, a factory of its
    /// project's own or the one the build writes there. In the context's own project the build's is not to be seen yet,
    /// since a generator never sees what another writes, so it is named as it will be; where it cannot be written, the
    /// generator that writes it says why, at the context, and this says nothing more.
    /// </summary>
    private static string? FactoryOf(INamedTypeSymbol contextType, List<INamedTypeSymbol> factories, Compilation compilation, out string? reason)
    {
        reason = null;
        var here = SymbolEqualityComparer.Default.Equals(contextType.ContainingAssembly, compilation.Assembly);

        if (!compilation.IsSymbolAccessibleWithin(contextType, compilation.Assembly))
        {
            reason = $"'{compilation.AssemblyName}' cannot see it; make it public";
            return null;
        }

        if (!here)
        {
            var own = DesignTimeFactories.FactoriesIn(
                factories.Where(factory => SymbolEqualityComparer.Default.Equals(factory.ContainingAssembly, compilation.Assembly)),
                contextType);
            if (own.Count > 0)
            {
                return OneOf(own, $"'{compilation.AssemblyName}', where dotnet ef run from it looks before the context's assembly,", compilation, out reason);
            }
        }

        var beside = DesignTimeFactories.FactoriesIn(
            factories.Where(factory => SymbolEqualityComparer.Default.Equals(factory.ContainingAssembly, contextType.ContainingAssembly)),
            contextType);
        if (beside.Count > 0)
        {
            return OneOf(beside, "its assembly", compilation, out reason);
        }

        if (here)
        {
            return DesignTimeFactories.Unwritable(contextType, compilation) is null ? DesignTimeFactories.QualifiedNameFor(contextType) : null;
        }

        reason = "its assembly has no design-time factory for it, which the build writes beside a marked context in a project the Supabase package's generator runs in";
        return null;
    }

    /// <summary>
    /// The one factory of <paramref name="candidates"/>, all found in one assembly that <paramref name="where"/> names,
    /// as generated code names it; null with the reason when there are several, which <c>dotnet ef</c> cannot choose
    /// between either, or when the generated code cannot create the one.
    /// </summary>
    private static string? OneOf(List<INamedTypeSymbol> candidates, string where, Compilation compilation, out string? reason)
    {
        reason = null;
        if (candidates.Count > 1)
        {
            reason = where + " has more than one design-time factory for it, "
                     + string.Join(" and ", candidates.Select(static factory => "'" + factory.ToDisplayString() + "'"))
                     + ", and the build cannot tell which one the export makes it with; mark that one [SupabaseMigrations] instead of the context";
            return null;
        }

        if (Unusable(candidates[0], compilation) is { } unusable)
        {
            reason = $"its design-time factory '{candidates[0].ToDisplayString()}' cannot be used: {unusable}";
            return null;
        }

        return candidates[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>The name of the call the registration writes, and of one a host may have written itself.</summary>
    private const string RegistrationMethod = "AddSupabaseMigrations";

    /// <summary>
    /// Whether this application declares an <c>AddSupabaseMigrations</c> of its own that <c>services.AddSupabaseMigrations()</c>
    /// would call: an extension method on <c>IServiceCollection</c> that needs nothing more. A host that wrote one before
    /// the build wrote it keeps it, and the registration leaves its own out, so the call stays the host's and is not
    /// ambiguous.
    /// </summary>
    private static bool DeclaresItsOwnRegistration(Compilation compilation)
        => compilation.GetSymbolsWithName(RegistrationMethod, SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(static method => method.IsExtensionMethod
                && method.Parameters.Length >= 1
                && method.Parameters[0].Type.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
                && method.Parameters.Skip(1).All(static rest => rest.IsOptional || rest.IsParams));

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
    /// name: the contributions a package declares with <c>[assembly: RowAccessContribution(typeof(X))]</c>.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ContributionsIn(IAssemblySymbol assembly, string attributeName)
        => assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName)
            .Select(static attribute => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol type ? type : null)
            .Where(static type => type is not null)
            .Select(static type => type!)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    /// <summary>
    /// This assembly, and every referenced assembly that references the Supabase package, where marked contexts and
    /// factories live, and, for the export, every one that references the toolkit's abstractions, where row access
    /// rules live. An application that only registers its contexts, every host, opens no more than its modules' storage.
    /// </summary>
    private static IEnumerable<IAssemblySymbol> Searched(Compilation compilation, bool rules)
    {
        yield return compilation.Assembly;

        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referenced.Modules.Any(module => module.ReferencedAssemblies.Any(identity =>
                    string.Equals(identity.Name, KnownTypes.SupabaseAssemblyName, StringComparison.Ordinal)
                    || (rules && string.Equals(identity.Name, AbstractionsAssemblyName, StringComparison.Ordinal)))))
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
        var roles = Strings(attribute, "To");

        // A column rule's columns, as its attribute names them: the core generator wrote no SQL for one it refused.
        var columns = Strings(attribute, "Columns");

        return new Rule(ClrName(aggregate), Humanize(type.Name), operations, new EquatableArray<string>(roles), sql, new EquatableArray<string>(columns));
    }

    /// <summary>The strings of the attribute's array argument <paramref name="name"/>, without the empty ones; none when it has no such argument.</summary>
    private static string[] Strings(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value is { Kind: TypedConstantKind.Array } values
            ? values.Values.Select(static each => each.Value as string).Where(static each => !string.IsNullOrWhiteSpace(each)).Select(static each => each!).ToArray()
            : [];

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
            return "it is no context, and it does not implement IDesignTimeDbContextFactory<TContext>";
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

    private static string Emit(EquatableArray<Source> sources, EquatableArray<Rule> rules, EquatableArray<Function> functions, ContributionsToExport contributions)
    {
        const string Supabase = "global::" + KnownTypes.SupabaseNamespace;
        const string Postgres = "global::" + KnownTypes.PostgresNamespace;

        var entries = Entries(sources);

        var ruleEntries = rules.Count == 0
            ? string.Empty
            : string.Join(
                "\n",
                rules.Select(rule =>
                    (rule.Columns.Count == 0
                        ? $"            {Postgres}.RowAccessRule.For({Literal(rule.Aggregate)}, {Literal(rule.Name)}, (global::{KnownTypes.AttributesNamespace}.RowOperations){rule.Operations}, {Literal(rule.Sql)}"
                        : $"            {Postgres}.RowAccessRule.ForColumns({Literal(rule.Aggregate)}, {Literal(rule.Name)}, new string[] {{ {string.Join(", ", rule.Columns.Select(Literal))} }}, {Literal(rule.Sql)}")
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

        // The packages' first, made in the classes written for them, then the application's own.
        var contributionEntries = string.Join(
            "\n",
            contributions.Made.Select(made => $"            new global::{GeneratedNamespace}.{made.ClassName}(),")
                .Concat(contributions.Listed.Select(listed => $"            new {listed}(),")));

        return $$"""
            // <auto-generated/>
            #nullable enable

            namespace DDDToolkit.EntityFramework.Supabase.Generated
            {
                /// <summary>
                /// Every context marked [SupabaseMigrations] this project references, and every factory marked so, found
                /// when it was compiled, and the hook the build's export step runs through.
                /// </summary>
                internal static class SupabaseMigrationSources
                {
                    /// <summary>The contexts whose migrations the build exports, each with the design-time factory that makes it.</summary>
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
                    /// The row access contributions the export asks what they write for each context: those the packages
                    /// this project references write, made in DDDToolkit.RowAccessContributionsOfPackages.g.cs, and those
                    /// it lists with [assembly: UseRowAccessContribution].
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

    /// <summary>One line per source, <c>SupabaseMigrationSource.For&lt;TContext, TFactory&gt;("Module"),</c>, joined with line feeds.</summary>
    private static string Entries(EquatableArray<Source> sources)
        => string.Join(
            "\n",
            sources.Select(source =>
                $"            global::{KnownTypes.SupabaseNamespace}.SupabaseMigrationSource.For<{source.Context}, {source.Factory}>({(source.Module is null ? "null" : Literal(source.Module))}),"));

    /// <summary>
    /// The class every application gets: the contexts it references, and <c>AddSupabaseMigrations()</c>, which registers
    /// them all for the start-up check. In the namespace named after the project's assembly, and internal, so a project
    /// that sees another's internals still names its own, as Tenancy's <c>TenancyPermissionsOfModules</c> is written.
    /// Where the application declares an <c>AddSupabaseMigrations()</c> of its own, <paramref name="hostRegisters"/>, the
    /// class holds the list alone.
    /// </summary>
    private static string EmitRegistration(string @namespace, EquatableArray<Source> sources, bool hostRegisters)
    {
        const string Supabase = "global::" + KnownTypes.SupabaseNamespace;
        const string Services = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";

        var call = hostRegisters ? "the host's own AddSupabaseMigrations()" : "<see cref=\"AddSupabaseMigrations\"/>";
        var listed = sources.Count == 0
            ? "    /// It found none: no project this one references marks a context or a factory, so <see cref=\"All\"/> is empty and\n"
              + (hostRegisters ? "    /// lists nothing" : "    /// <see cref=\"AddSupabaseMigrations\"/> registers nothing") + ". A marked context is in it as soon as this project references it.\n"
            : "    /// It found them in the projects this one references, and in this one:\n"
              + string.Concat(sources.Select((source, index) =>
                  $"    /// <c>{InDoc(source.Context)}</c>, made by <c>{InDoc(source.Factory)}</c>{(index == sources.Count - 1 ? "." : ",")}\n"))
              + "    /// A context marked later is in it as soon as this project references it.\n";
        var followed = "    /// The list follows the references, not the registrations: a context of a project this one references is in it\n"
                       + "    /// whether the host's container registers the context or not, and the check names one it cannot resolve. A host\n"
                       + "    /// that uses some of them registers those one by one, with AddSupabaseMigrations&lt;TContext, TFactory&gt;().\n";
        var kept = hostRegisters
            ? "    /// This application declares an AddSupabaseMigrations() of its own, which is kept: the class holds the list alone,\n"
              + "    /// which that method may register with AddSupabaseMigrations(source) for each.\n"
            : string.Empty;

        // The call itself, left out where the host wrote its own, which a second one would make ambiguous.
        var method = hostRegisters
            ? string.Empty
            : $$"""


                        /// <summary>
                        /// Registers every context of <see cref="All"/> as one whose migrations Supabase applies, which brings the
                        /// start-up check supabase.migrations-applied once, as AddSupabaseMigrations(source) does for one context.
                        /// Call it once, in the host; a context registered twice is registered once. The check asks each of them
                        /// for its history, so the host's container must resolve every one.
                        /// </summary>
                        /// <param name="services">The host's services.</param>
                        public static {{Services}} AddSupabaseMigrations(this {{Services}} services)
                        {
                            foreach (var source in All())
                            {
                                {{Supabase}}.DependencyInjection.AddSupabaseMigrations(services, source);
                            }

                            return services;
                        }
                """;

        return $$"""
            // <auto-generated/>
            #nullable enable

            namespace {{@namespace}}
            {
                /// <summary>
                /// Every context marked [SupabaseMigrations] this application references, and every factory marked so, each
                /// with the design-time factory dotnet ef makes it with: what the host registers for the start-up check that
                /// Supabase applied every migration, with {{call}}.
                /// </summary>
                /// <remarks>
                /// Written by the Supabase package's generator into every application that is not a test project.
            {{listed}}{{followed}}{{kept}}    /// </remarks>
                {{DesignTimeFactories.GeneratedCode}}
                internal static class {{RegistrationClass}}
                {
                    /// <summary>The contexts, each with its factory and the module its files are named after.</summary>
                    public static global::System.Collections.Generic.IReadOnlyList<{{Supabase}}.SupabaseMigrationSource> All()
                        => new {{Supabase}}.SupabaseMigrationSource[]
                        {
            {{Entries(sources)}}
                        };{{method}}
                }
            }

            """
            .Replace("\r\n", "\n");
    }

    /// <summary>A name inside an XML doc comment, with the <c>global::</c> generated code names it by left off.</summary>
    private static string InDoc(string name)
        => CodeWriter.XmlText(name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring("global::".Length) : name);

    private static string Literal(string value)
        => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>One context: itself, the factory that makes it, and the module the factory's assembly declares, if any.</summary>
    private sealed record Source(string Context, string Factory, string? Module);

    /// <summary>
    /// One row access rule: the aggregate's CLR name, the policy's name, what it allows, for whom, its SQL, and the
    /// columns of a column rule, none for a rule about whole rows.
    /// </summary>
    private sealed record Rule(string Aggregate, string Name, int Operations, EquatableArray<string> Roles, string Sql, EquatableArray<string> Columns);

    /// <summary>
    /// One access function: the aggregate's CLR name, the function's name, its SQL, the SQL types of the
    /// parameters it takes after the key, and its shape, as <c>AccessFunctionShape</c> numbers it.
    /// </summary>
    private sealed record Function(string Aggregate, string Name, string Sql, string Parameters, int Shape);

    /// <summary>
    /// What the generator found: what the project is to it, the namespace the registration goes in, whether the host
    /// declares an <c>AddSupabaseMigrations()</c> of its own, the sources to register and export, the rules and access
    /// functions to export, the contributions it hands the export, the packages' and the application's own, and the
    /// diagnostics to report.
    /// </summary>
    private sealed record Discovery(
        Role Role,
        string Namespace,
        bool HostRegisters,
        EquatableArray<Source> Sources,
        EquatableArray<Rule> Rules,
        EquatableArray<Function> Functions,
        ContributionsToExport Contributions,
        EquatableArray<DiagnosticInfo> Diagnostics)
    {
        public static readonly Discovery None = new(Role.None, string.Empty, false, EquatableArray<Source>.Empty, EquatableArray<Rule>.Empty, EquatableArray<Function>.Empty, ContributionsToExport.None, EquatableArray<DiagnosticInfo>.Empty);
    }
}
