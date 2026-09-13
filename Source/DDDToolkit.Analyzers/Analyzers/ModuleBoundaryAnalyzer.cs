using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Analyzers.Analyzers;

/// <summary>
/// Keeps module boundaries where their owners drew them. Two rules, both silent until an assembly says
/// it is a module with <c>[assembly: Module("Name")]</c>:
/// <list type="bullet">
/// <item>
/// <description>
/// DDD00022, where this module names a type that belongs to another module and is not part of that
/// module's published contract.
/// </description>
/// </item>
/// <item>
/// <description>
/// DDD00023, where an entity or aggregate root of this module stores another module's entity, published
/// or not.
/// </description>
/// </item>
/// </list>
/// <para>
/// A module is an assembly, not a namespace. That is the whole of what a Roslyn analyzer can check:
/// it sees this compilation plus the metadata of everything it references, and an assembly attribute is
/// the only module declaration that survives into metadata for the other side to read.
/// </para>
/// <para>
/// Both rules are warnings. The rule they state is a design decision, not a fact about whether the code
/// compiles, and a codebase adopting modules wants to see the list before it has to fix it. Turn either
/// into a build break with <c>&lt;WarningsAsErrors&gt;$(WarningsAsErrors);DDD00022&lt;/WarningsAsErrors&gt;</c>
/// once the list is empty.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModuleBoundaryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Prints a referenced type as <c>Ordering.Order</c> rather than <c>global::Ordering.Order</c>.</summary>
    private static readonly SymbolDisplayFormat QualifiedName =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.TypeIsNotPublishedByItsModule,
        DiagnosticDescriptors.DoNotHoldAnotherModulesEntity,
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    /// <summary>
    /// Everything below this point costs nothing in a project that has not opted in: an assembly with no
    /// <c>[Module]</c> registers no actions at all.
    /// </summary>
    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var modules = ModuleBoundary.Map.For(context.Compilation);
        if (modules is null)
        {
            return;
        }

        context.RegisterSemanticModelAction(semanticModel => AnalyzeNames(semanticModel, modules));
        context.RegisterSymbolAction(symbol => AnalyzeStoredState(symbol, modules), SymbolKind.NamedType);
    }

    // ------------------------------------------------------------------ DDD00022

    /// <summary>
    /// Reports every place this file names a type of another module that the module does not publish.
    /// <para>
    /// The unit is a name in the source, which is the one thing a reader can act on: you either write
    /// the type's name or you do not. It walks the file once rather than registering an action per
    /// syntax kind, so a name in a base list, an attribute, a <c>typeof</c>, a generic argument and a
    /// static call are all the same case.
    /// </para>
    /// </summary>
    private static void AnalyzeNames(SemanticModelAnalysisContext context, ModuleBoundary.Map modules)
    {
        var semanticModel = context.SemanticModel;
        var root = semanticModel.SyntaxTree.GetRoot(context.CancellationToken);

        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (IsQualifierOfAnotherName(name, semanticModel, context.CancellationToken))
            {
                continue;
            }

            var referenced = ReferencedType(semanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol);
            if (referenced is null)
            {
                continue;
            }

            var owner = modules.OtherModuleOf(referenced);
            if (owner is null || ModuleBoundary.IsPublished(referenced))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.TypeIsNotPublishedByItsModule,
                name.GetLocation(),
                referenced.ToDisplayString(QualifiedName),
                owner,
                modules.Module));
        }
    }

    /// <summary>
    /// The type a name refers to, or null when it refers to something else. A constructor is included
    /// because that is what an attribute's name and a <c>new</c> expression resolve to; an ordinary
    /// member is not, because the type that declares it may be a base type the author never named.
    /// </summary>
    private static INamedTypeSymbol? ReferencedType(ISymbol? symbol) => symbol switch
    {
        INamedTypeSymbol named => named.OriginalDefinition,
        IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType.OriginalDefinition,
        _ => null,
    };

    /// <summary>
    /// Whether this name only qualifies a more specific one, as <c>Customer</c> does in
    /// <c>Customer.Address</c>. Reporting the qualifier as well would put two warnings on one reference
    /// to one nested type. A qualifier of a <em>member</em> is not skipped: in
    /// <c>CustomerService.Find()</c> the type name is the only part worth reporting.
    /// </summary>
    private static bool IsQualifierOfAnotherName(SimpleNameSyntax name, SemanticModel semanticModel, CancellationToken cancellationToken)
        => name.Parent switch
        {
            QualifiedNameSyntax qualified => qualified.Left == name,
            MemberAccessExpressionSyntax access =>
                access.Expression == name && semanticModel.GetSymbolInfo(access, cancellationToken).Symbol is ITypeSymbol,
            _ => false,
        };

    // ------------------------------------------------------------------ DDD00023

    /// <summary>
    /// Reports every field or property of an entity of this module that stores an entity of another one.
    /// Stored state only, the same reading <see cref="AggregateBoundary"/> takes for DDD00021.
    /// </summary>
    private static void AnalyzeStoredState(SymbolAnalysisContext context, ModuleBoundary.Map modules)
    {
        var entity = (INamedTypeSymbol)context.Symbol;
        if (!ModuleBoundary.IsEntity(entity))
        {
            return;
        }

        foreach (var member in entity.GetMembers())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var stored = StoredState.StoredTypeOf(member);
            if (stored is null)
            {
                continue;
            }

            // The generated partial part of an entity declares the backing field of every collection
            // property the author wrote. Reporting it would repeat the property's warning on a file
            // nobody edits.
            var location = ModuleBoundary.AuthoredLocationOf(member);
            if (location is null)
            {
                continue;
            }

            string? owner = null;
            var held = StoredState.Find(stored, candidate =>
            {
                if (!ModuleBoundary.IsEntity(candidate.OriginalDefinition))
                {
                    return false;
                }

                owner = modules.OtherModuleOf(candidate.OriginalDefinition);
                return owner is not null;
            });

            if (held is null || owner is null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DoNotHoldAnotherModulesEntity,
                location,
                entity.Name,
                member.Name,
                held.Value.Type.Name,
                owner));
        }
    }
}
