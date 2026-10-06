using System;
using System.Collections.Generic;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.EntityFramework.Supabase.Analyzers;

/// <summary>
/// The design-time factory the build writes for a context marked <c>[SupabaseMigrations]</c>: what it is called, where
/// it goes, and whether it can be written. Worked out here once, for the generator that writes the factory beside the
/// context and for the one that lists it in the application, which never sees what the other wrote in the same
/// compilation and so has to arrive at the same name by itself.
/// </summary>
internal static class DesignTimeFactories
{
    /// <summary>What every context derives from.</summary>
    public const string DbContext = "Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>The options a context is made with, of no context in particular.</summary>
    public const string DbContextOptions = "Microsoft.EntityFrameworkCore.DbContextOptions";

    /// <summary>The options of one context, which the factory hands its context.</summary>
    public const string DbContextOptionsOf = "Microsoft.EntityFrameworkCore.DbContextOptions`1";

    /// <summary>Npgsql's <c>UseNpgsql</c>, which the factory builds the context on.</summary>
    public const string NpgsqlExtensions = "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions";

    /// <summary>The class of <c>UseDDDToolkitDesignTime</c>, in DDDToolkit.EntityFramework.</summary>
    public const string ToolkitWiring = "DDDToolkit.EntityFramework.DependencyInjection";

    /// <summary>The toolkit's call for what a context made without services gets of it.</summary>
    public const string DesignTimeCall = "UseDDDToolkitDesignTime";

    /// <summary>What the factory's name adds to the context's: <c>OrderingContextDesignTimeFactory</c>.</summary>
    public const string Suffix = "DesignTimeFactory";

    /// <summary>
    /// The attribute on every class this package's generators write, which tells code the build wrote from code a
    /// developer wrote: to a reader, to coverage, and to an architecture test that holds a project's own code to a
    /// rule the generated code is exempt from, as such a test tells the Mediator's generated code by the same attribute.
    /// </summary>
    public static readonly string GeneratedCode =
        "[global::System.CodeDom.Compiler.GeneratedCode(\"DDDToolkit.EntityFramework.Supabase.Analyzers\", \""
        + (typeof(DesignTimeFactories).Assembly.GetName().Version?.ToString() ?? "0.0.0.0")
        + "\")]";

    /// <summary>Whether <paramref name="type"/> is a context: a class that derives from <c>DbContext</c>.</summary>
    public static bool IsContext(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == DbContext)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The factory's name: the context's, with the names of the types it is nested in before it, and
    /// <see cref="Suffix"/> after it. It goes in the context's namespace, beside the context.
    /// </summary>
    public static string NameFor(INamedTypeSymbol context)
    {
        var name = context.Name;
        for (var outer = context.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            name = outer.Name + name;
        }

        return name + Suffix;
    }

    /// <summary>The factory's name as generated code names it: <c>global::Shop.Ordering.OrderingContextDesignTimeFactory</c>.</summary>
    public static string QualifiedNameFor(INamedTypeSymbol context)
        => "global::" + (context.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() + "." : string.Empty) + NameFor(context);

    /// <summary>
    /// The contexts <paramref name="factory"/> makes, as the <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c> it
    /// implements say: one for most factories, every one of them for a factory that makes several, as <c>dotnet ef</c>
    /// takes them all, and none for a type that is no such factory.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> ContextsMadeBy(INamedTypeSymbol factory)
        => factory.AllInterfaces
            .Where(static candidate =>
                candidate.OriginalDefinition.MetadataName == "IDesignTimeDbContextFactory`1"
                && candidate.OriginalDefinition.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore.Design")
            .Select(static candidate => candidate.TypeArguments[0] as INamedTypeSymbol)
            .Where(static context => context is not null)
            .Select(static context => context!);

    /// <summary>Whether <paramref name="factory"/> makes <paramref name="context"/>, among whatever else it makes.</summary>
    public static bool Makes(INamedTypeSymbol factory, INamedTypeSymbol context)
        => ContextsMadeBy(factory).Any(made => SymbolEqualityComparer.Default.Equals(made, context));

    /// <summary>
    /// Whether <c>dotnet ef</c> would make the context through <paramref name="factory"/>: it takes every class of an
    /// assembly that is neither abstract nor an open generic and implements the interface, and creates it with its
    /// parameterless constructor. So does this, and a factory of the developer's own that it finds is the one that wins.
    /// </summary>
    public static bool IsUsableByEntityFramework(INamedTypeSymbol factory)
        => factory.TypeKind == TypeKind.Class && !factory.IsAbstract && !factory.IsStatic && !factory.IsGenericType;

    /// <summary>
    /// Why the build cannot write a factory for <paramref name="context"/>, as DDD00031 says it after
    /// <c>is marked [SupabaseMigrations] but</c>, or null when it can. A context the build cannot write one for, and
    /// that has no factory of its own beside it, is made by nothing, so its migrations are not exported.
    /// </summary>
    public static string? Unwritable(INamedTypeSymbol context, Compilation compilation)
    {
        if (context.IsAbstract)
        {
            return "it is abstract, so no design-time factory can make it; mark the context the application uses";
        }

        if (context.IsGenericType)
        {
            return "it is generic, and the build would not know what to close it with";
        }

        if (!compilation.IsSymbolAccessibleWithin(context, compilation.Assembly))
        {
            return "it is private to the type it is in, where the design-time factory the build writes beside it cannot make it, and it has no factory of its own";
        }

        var constructors = OptionsConstructors(context, compilation);
        if (constructors.Count == 0)
        {
            return "it has no constructor that takes its options alone, DbContextOptions<" + context.Name + ">, which the design-time factory the build writes makes it with, and no factory of its own beside it";
        }

        if (RequiredMembersOf(context) is { Count: > 0 } required && !constructors.Any(SetsRequiredMembers))
        {
            return "it has required members, " + string.Join(", ", required) + ", which the design-time factory the build writes cannot set, "
                   + "since no constructor that takes its options alone says it sets them with [SetsRequiredMembers], and it has no factory of its own beside it";
        }

        if (compilation.GetTypeByMetadataName(NpgsqlExtensions) is null)
        {
            return "its project does not reference Npgsql.EntityFrameworkCore.PostgreSQL, which the design-time factory the build writes builds it on, and it has no factory of its own beside it";
        }

        return null;
    }

    /// <summary>
    /// Whether the factory the build writes calls <c>UseDDDToolkitDesignTime()</c>: where the project references
    /// DDDToolkit.EntityFramework at a version that has it, which is where a host wires the context with
    /// <c>UseDDDToolkit</c> and DDD00071 holds a factory written by hand to the same call. Elsewhere the toolkit does not
    /// wire the context, and the history stays where Entity Framework keeps it, in the factory as in the host.
    /// </summary>
    public static bool CallsTheToolkit(Compilation compilation)
        => compilation.GetTypeByMetadataName(ToolkitWiring) is { } wiring
           && wiring.GetMembers(DesignTimeCall).OfType<IMethodSymbol>().Any(static method => method.IsStatic && method.Parameters.Length == 1);

    /// <summary>
    /// The constructors of the context that the factory, beside it in the same assembly, can call with the options
    /// alone, as <c>new TContext(options)</c>: not private or protected, the options first, <c>DbContextOptions&lt;TContext&gt;</c>
    /// or <c>DbContextOptions</c>, and after them nothing the call has to give, only optional parameters and a
    /// <c>params</c> one.
    /// </summary>
    private static List<IMethodSymbol> OptionsConstructors(INamedTypeSymbol context, Compilation compilation)
    {
        var generic = compilation.GetTypeByMetadataName(DbContextOptionsOf);
        var plain = compilation.GetTypeByMetadataName(DbContextOptions);

        return context.InstanceConstructors.Where(constructor =>
                constructor.Parameters.Length >= 1
                && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                && (SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, plain)
                    || (constructor.Parameters[0].Type is INamedTypeSymbol parameter
                        && SymbolEqualityComparer.Default.Equals(parameter.OriginalDefinition, generic)
                        && SymbolEqualityComparer.Default.Equals(parameter.TypeArguments[0], context)))
                && constructor.Parameters.Skip(1).All(static rest => rest.IsOptional || rest.IsParams))
            .ToList();
    }

    /// <summary>
    /// The <c>required</c> members of the context and of the classes it derives from, by name:
    /// <c>new TContext(options)</c> compiles only where the constructor says it sets them, with
    /// <c>[SetsRequiredMembers]</c>.
    /// </summary>
    private static List<string> RequiredMembersOf(INamedTypeSymbol context)
    {
        var required = new List<string>();
        for (var type = context; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true } && !required.Contains(member.Name))
                {
                    required.Add(member.Name);
                }
            }
        }

        return required;
    }

    /// <summary>Whether <paramref name="constructor"/> says it sets every required member, with <c>[SetsRequiredMembers]</c>.</summary>
    private static bool SetsRequiredMembers(IMethodSymbol constructor)
        => constructor.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");

    /// <summary>
    /// The accessibility the factory is declared with: public when the context can be seen from every project that
    /// references its own, so the application that lists the factory names it, and internal otherwise, as the context.
    /// </summary>
    public static string AccessibilityFor(INamedTypeSymbol context)
    {
        for (ISymbol? symbol = context; symbol is INamedTypeSymbol type; symbol = type.ContainingType)
        {
            if (type.DeclaredAccessibility != Accessibility.Public)
            {
                return "internal";
            }
        }

        return "public";
    }

    /// <summary>
    /// The factory written beside a context: on Npgsql, with a connection string that names no server, since neither
    /// <c>dotnet ef</c> nor the export opens a connection, and with <c>UseDDDToolkitDesignTime()</c> where
    /// <paramref name="callsTheToolkit"/>.
    /// </summary>
    public static string Write(FactoryToWrite factory, bool callsTheToolkit)
    {
        var context = factory.Context;
        var builder = "new global::Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<" + context + ">()";
        var options = "global::" + NpgsqlExtensions + ".UseNpgsql(" + builder + ", \"Host=unused\")";
        if (callsTheToolkit)
        {
            options = "global::" + ToolkitWiring + "." + DesignTimeCall + "(" + options + ")";
        }

        var writer = new CodeWriter().Header();
        if (factory.Namespace.Length > 0)
        {
            writer.Line("namespace " + factory.Namespace + ";");
            writer.Line();
        }

        writer.Line("/// <summary>");
        writer.Line("/// How <c>dotnet ef</c> and the Supabase export make <see cref=\"" + context + "\"/>, which is marked");
        writer.Line("/// <c>[SupabaseMigrations]</c>: on Postgres, through Npgsql, with a connection string that names no server, since");
        writer.Line("/// neither of them opens a connection.");
        if (callsTheToolkit)
        {
            writer.Line("/// <c>UseDDDToolkitDesignTime()</c> keeps the migration history in the context's default schema, where a host");
            writer.Line("/// that wires the context with <c>UseDDDToolkit</c> reads it.");
        }

        writer.Line("/// </summary>");
        writer.Line("/// <remarks>");
        writer.Line("/// Written by the build because the context is marked, and only while no factory of the project's own makes the");
        writer.Line("/// context: write an <c>IDesignTimeDbContextFactory</c> for it in this project, and the build writes this one no");
        writer.Line("/// more and uses that one. The migrations are found where Entity Framework looks when it is told nothing else, in");
        writer.Line("/// the context's own assembly.");
        writer.Line("/// </remarks>");
        writer.Line(GeneratedCode);
        using (writer.Block(factory.Accessibility + " sealed class " + factory.Name + " : global::Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<" + context + ">"))
        {
            writer.Line("/// <summary>A new context on options that point nowhere; the caller disposes it.</summary>");
            writer.Line("/// <param name=\"args\">What <c>dotnet ef</c> passes after <c>--</c>; not read.</param>");
            writer.Line("public " + context + " CreateDbContext(string[] args)");
            writer.Line("    => new " + context + "(" + options + ".Options);");
        }

        return writer.ToString();
    }

    /// <summary>
    /// A factory to write: the context it makes, by its fully qualified name, the namespace it goes in, its own name,
    /// and the accessibility it is declared with.
    /// </summary>
    public sealed record FactoryToWrite(string Context, string Namespace, string Name, string Accessibility);

    /// <summary>The factory to write for <paramref name="context"/>.</summary>
    public static FactoryToWrite ToWrite(INamedTypeSymbol context)
        => new(
            context.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            context.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() : string.Empty,
            NameFor(context),
            AccessibilityFor(context));

    /// <summary>
    /// The classes of <paramref name="assembly"/> that <c>dotnet ef</c> would make <paramref name="context"/> through,
    /// in the order of their names.
    /// </summary>
    public static List<INamedTypeSymbol> FactoriesIn(IEnumerable<INamedTypeSymbol> types, INamedTypeSymbol context)
        => types
            .Where(type => IsUsableByEntityFramework(type) && Makes(type, context))
            .OrderBy(static type => type.ToDisplayString(), StringComparer.Ordinal)
            .ToList();
}
