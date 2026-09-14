using System.Collections.Immutable;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Analyzers.Analyzers;

/// <summary>
/// Finds the rule nobody will ever run: an <c>IInvariant&lt;T&gt;</c> implementation declared anywhere
/// other than inside an entity (DDD00024).
/// <para>
/// This is an analyzer rather than a generator diagnostic because of where it has to look. The entity
/// generator finds rules by asking the entity it is already generating for its nested types, which is
/// the whole point of nesting them; a rule written outside an entity is by definition not among those,
/// and only a pass over the compilation's own types can see it at all. The other three invariant rules
/// (DDD00025 to DDD00027) are reported by the generator instead, where the same pass that decides what
/// to emit decides what to complain about, so the two cannot drift apart.
/// </para>
/// <para>
/// It is a warning. The code compiles and its unit tests pass; what it does not do is run when the
/// entity is saved, and an author who really did mean to hand the rule to something else can say so
/// with a suppression.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvariantAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.InvariantMustBeNestedInItsSubject,
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // An abstract or open generic type is a base an author shares between rules, not a rule that was
        // meant to run on its own, so it is none of this rule's business wherever it lives.
        if (!Invariants.CanBeARule(type))
        {
            return;
        }

        var subject = Invariants.SubjectOf(type);
        if (subject is null)
        {
            return;
        }

        // Nested in an entity is the shape this whole design is about. Whether it is nested in the right
        // one is the generator's question (DDD00025), which knows what it is generating.
        if (type.ContainingType is { } container && IsEntity(container))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.InvariantMustBeNestedInItsSubject,
            type.Locations.Length > 0 ? type.Locations[0] : Location.None,
            type.Name,
            subject.Name));
    }

    private static bool IsEntity(INamedTypeSymbol type)
        => DefinitionFactory.HasAttribute(type, KnownTypes.EntityAttribute)
           || DefinitionFactory.HasAttribute(type, KnownTypes.AggregateRootAttribute);
}
