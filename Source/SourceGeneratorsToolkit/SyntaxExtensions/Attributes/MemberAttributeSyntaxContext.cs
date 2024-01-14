using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace SourceGeneratorsToolkit.SyntaxExtensions.Attributes;
public readonly struct MemberAttributeSyntaxContext(
    SyntaxNode targetNode,
    ISymbol targetSymbol,
    SemanticModel semanticModel,
    ImmutableArray<(AttributeSyntaxContext attribute, bool match)> attributes)
{
    /// <summary>
    /// The syntax node the attribute is attached to.  For example, with <c>[CLSCompliant] class C { }</c> this would
    /// the class declaration node.
    /// </summary>
    public SyntaxNode TargetNode { get; } = targetNode;

    /// <summary>
    /// The symbol that the attribute is attached to.  For example, with <c>[CLSCompliant] class C { }</c> this would be
    /// the <see cref="INamedTypeSymbol"/> for <c>"C"</c>.
    /// </summary>
    public ISymbol TargetSymbol { get; } = targetSymbol;

    /// <summary>
    /// Semantic model for the file that <see cref="TargetNode"/> is contained within.
    /// </summary>
    public SemanticModel SemanticModel { get; } = semanticModel;

    public ImmutableArray<(AttributeSyntaxContext attribute, bool match)> Attributes { get; } = attributes;
}
