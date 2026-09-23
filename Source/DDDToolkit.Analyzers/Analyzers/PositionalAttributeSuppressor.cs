using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Analyzers.Analyzers;

/// <summary>
/// Silences CS0657 for a <c>[property: ...]</c> or <c>[field: ...]</c> attribute on a parameter of a
/// positional <c>[ValueObject]</c> record, once the generator has carried it over.
/// <para>
/// The compiler only honours those targets while it synthesizes the property itself. The value object
/// generator declares the property instead, as <c>protected init</c>, so the compiler warns that the
/// attribute is ignored. It is not: the generator copied it onto the property it declared. The warning
/// is suppressed only when every attribute in the list really is on that property (or its backing
/// field), so an attribute on a property declared by hand, which does get lost, still warns.
/// </para>
/// </summary>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class PositionalAttributeSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor CarriedOver = new(
        id: "DDDS0001",
        suppressedDiagnosticId: "CS0657",
        justification: "The value object generator declares this property itself and carries the attribute over to it.");

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = [CarriedOver];

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (IsCarriedOver(context, diagnostic))
            {
                context.ReportSuppression(Suppression.Create(CarriedOver, diagnostic));
            }
        }
    }

    private static bool IsCarriedOver(SuppressionAnalysisContext context, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree is not { } tree)
        {
            return false;
        }

        var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        if (node.FirstAncestorOrSelf<AttributeListSyntax>() is not { Target: { } target, Parent: ParameterSyntax parameter } list
            || parameter.Parent?.Parent is not RecordDeclarationSyntax record)
        {
            return false;
        }

        var model = context.GetSemanticModel(tree);
        if (model.GetDeclaredSymbol(record, context.CancellationToken) is not INamedTypeSymbol type
            || !DefinitionFactory.HasAttribute(type, KnownTypes.ValueObjectAttribute))
        {
            return false;
        }

        var property = type.GetMembers(parameter.Identifier.ValueText).OfType<IPropertySymbol>().FirstOrDefault();
        ISymbol? carrier = target.Identifier.ValueText switch
        {
            "property" => property,
            "field" => type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property)),
            _ => null,
        };

        if (carrier is null)
        {
            return false;
        }

        var carried = carrier.GetAttributes().Select(attribute => attribute.AttributeClass).ToList();
        return list.Attributes.All(attribute =>
            model.GetSymbolInfo(attribute, context.CancellationToken).Symbol?.ContainingType is { } attributeClass
            && carried.Contains(attributeClass, SymbolEqualityComparer.Default));
    }
}
