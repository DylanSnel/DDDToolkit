using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.EntityFramework.Supabase.Analyzers;

/// <summary>
/// Writes the design-time factory of every context its project marks <c>[SupabaseMigrations]</c>, beside the context,
/// in a file named after the factory:
/// <code>
/// // Shop.Ordering.OrderingContextDesignTimeFactory.g.cs, in the project that declares the context
/// public sealed class OrderingContextDesignTimeFactory : IDesignTimeDbContextFactory&lt;OrderingContext&gt;
/// {
///     public OrderingContext CreateDbContext(string[] args)
///         =&gt; new OrderingContext(new DbContextOptionsBuilder&lt;OrderingContext&gt;().UseNpgsql("Host=unused").UseDDDToolkitDesignTime().Options);
/// }
/// </code>
/// <para>
/// The Supabase export always builds on Postgres and never connects, and the toolkit says what else a context made
/// without services needs with <c>UseDDDToolkitDesignTime()</c>, so the factory every module used to write by hand was
/// the same eight lines. The marker on the context is the visible line it is written from. <c>dotnet ef</c> finds it
/// as it finds a factory written by hand: Entity Framework looks for one in the context's own assembly, which is where
/// this one is, and the export and <c>AddSupabaseMigrations()</c> name it in the application that lists the contexts.
/// </para>
/// <para>
/// A factory of the project's own for the context wins: where the project declares a class <c>dotnet ef</c> would make
/// the context through, a concrete <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c>, among the contexts it makes,
/// nothing is written and nothing is said. A factory in another project is not seen from here. One in the application
/// that composes the modules is what <c>dotnet ef</c> takes before this one when that application is its startup
/// project, and what the export and <c>AddSupabaseMigrations()</c> there make the context with; one anywhere else, a
/// migrations project of its own say, is what the export uses once it is marked itself.
/// </para>
/// <para>
/// Where no factory can be written and the project has none of its own, the context is made by nothing and its
/// migrations would not be exported: DDD00031, at the context, with the reason. A context the toolkit does not wire,
/// in a project that does not reference DDDToolkit.EntityFramework, gets the factory without
/// <c>UseDDDToolkitDesignTime()</c>, and keeps its history where Entity Framework keeps it, as DDD00071 lets a factory
/// written by hand do there.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SupabaseDesignTimeFactoryGenerator : IIncrementalGenerator
{
    private const string AttributeName = KnownTypes.SupabaseNamespace + "." + KnownTypes.SupabaseMigrationsAttributeName;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The marked contexts of this project. A marked factory is the other generator's, as it was before contexts
        // could be marked, and is passed over here.
        var marked = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, _) => Inspect(syntaxContext))
            .Where(static context => context is not null)
            .Select(static (context, _) => context!)
            .Collect();

        // The contexts this project has a factory of its own for, by their fully qualified names: each of those wins,
        // so nothing is written for it. Read off each class that names a base type, and kept as plain text. A factory
        // that makes several contexts makes each of them, as dotnet ef reads it.
        var owned = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } or RecordDeclarationSyntax { BaseList: not null },
                transform: static (syntaxContext, cancellationToken) => OwnFactoryOf(syntaxContext, cancellationToken))
            .SelectMany(static (made, _) => made)
            .Collect()
            .Select(static (made, _) => new EquatableArray<string>(made.Distinct(StringComparer.Ordinal).OrderBy(static each => each, StringComparer.Ordinal).ToArray()));

        var callsTheToolkit = context.CompilationProvider.Select(static (compilation, _) => DesignTimeFactories.CallsTheToolkit(compilation));

        context.RegisterSourceOutput(marked.Combine(owned).Combine(callsTheToolkit), static (production, input) =>
            Execute(production, input.Left.Left, input.Left.Right, input.Right));
    }

    /// <summary>
    /// One marked context of this project: the factory the build would write for it, or why it cannot, and where the
    /// context is declared, which a report points at.
    /// </summary>
    /// <param name="Context">The context by its fully qualified name, as a factory of the project's own names it.</param>
    /// <param name="Display">The context as a message names it.</param>
    /// <param name="Factory">What would be written; null when nothing can be.</param>
    /// <param name="Problem">Why nothing can be written, as DDD00031 says it; null when the factory can be.</param>
    /// <param name="Location">The context's name in its declaration.</param>
    private sealed record MarkedContext(string Context, string Display, DesignTimeFactories.FactoryToWrite? Factory, string? Problem, LocationInfo? Location);

    private static MarkedContext? Inspect(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || !DesignTimeFactories.IsContext(type))
        {
            return null;
        }

        var problem = DesignTimeFactories.Unwritable(type, context.SemanticModel.Compilation);
        var location = context.TargetNode is ClassDeclarationSyntax declaration ? LocationInfo.From(declaration.Identifier) : LocationInfo.From(type);
        return new MarkedContext(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            problem is null ? DesignTimeFactories.ToWrite(type) : null,
            problem,
            location);
    }

    /// <summary>
    /// The contexts the class at <paramref name="context"/> makes, by their fully qualified names, when <c>dotnet ef</c>
    /// would make them through the class: every one of its <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c>, since
    /// <c>dotnet ef</c> takes each; none otherwise.
    /// </summary>
    private static EquatableArray<string> OwnFactoryOf(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol type
            || !DesignTimeFactories.IsUsableByEntityFramework(type))
        {
            return EquatableArray<string>.Empty;
        }

        return new EquatableArray<string>(DesignTimeFactories.ContextsMadeBy(type).Select(static made => made.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private static void Execute(SourceProductionContext production, ImmutableArray<MarkedContext> marked, EquatableArray<string> owned, bool callsTheToolkit)
    {
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in marked.OrderBy(static each => each.Context, StringComparer.Ordinal))
        {
            // A partial context marked in two of its parts is one context.
            if (!written.Add(context.Context) || owned.Contains(context.Context))
            {
                continue;
            }

            if (context.Factory is null)
            {
                production.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.SupabaseMigrationsFactoryUnusable,
                    context.Location?.ToLocation() ?? Location.None,
                    context.Display,
                    context.Problem));
                continue;
            }

            var hint = (context.Factory.Namespace.Length > 0 ? context.Factory.Namespace + "." : string.Empty) + context.Factory.Name + ".g.cs";
            production.AddSource(hint, SourceText.From(DesignTimeFactories.Write(context.Factory, callsTheToolkit), Encoding.UTF8));
        }
    }
}
