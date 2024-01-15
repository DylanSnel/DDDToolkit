using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace SourceGeneratorsToolkit.Providers.Contexts;
public readonly struct TypeAttributeSyntaxContext(
    SyntaxNode targetNode,
    ISymbol targetSymbol,
    SemanticModel semanticModel,
    ImmutableArray<(AttributeSyntaxContext attribute, bool match)> attributes)
{
    public SyntaxNode TargetNode { get; } = targetNode;

    public ISymbol TargetSymbol { get; } = targetSymbol;

    public SemanticModel SemanticModel { get; } = semanticModel;

    public ImmutableArray<(AttributeSyntaxContext attribute, bool match)> Attributes { get; } = attributes;
}
