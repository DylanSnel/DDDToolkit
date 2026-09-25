using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00036: two events of one module under one name and version. The fix pins another name on the
/// event it is invoked on, the name the generator suggests: the class name with the class's namespace or
/// containing type in front, so <c>Ordering.Domain.Returns.OrderPlaced</c> gets
/// <c>[DomainEventName("ordering.returns-order-placed")]</c>.
/// <list type="bullet">
///   <item><description>A domain event gets <c>[DomainEventName("...")]</c>, which names it where it is stored and,
///   when it is published as it stands, where it is published.</description></item>
///   <item><description>A contract that is not a domain event gets the name in its <c>[IntegrationEvent]</c>.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// There is no fix-all. The diagnostic is reported on both events of a pair, and pinning both would only
/// move the collision; which one keeps the conventional name is the author's choice. An event whose name
/// is pinned already is offered nothing either: that name was chosen by hand, and so is the next one.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EventNameCodeFixProvider))]
public sealed class EventNameCodeFixProvider : CodeFixProvider
{
    private const string NameTaken = "DDD00036";

    // The keys the generator writes into the diagnostic, EventNamesGenerator.SuggestedNameProperty and
    // PinWithProperty. The generator assembly is not referenced here, so they are repeated.
    private const string SuggestedNameProperty = "SuggestedName";
    private const string PinWithProperty = "PinWith";

    private const string AttributesNamespace = "DDDToolkit.Abstractions.Attributes";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(NameTaken);

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(SuggestedNameProperty, out var name) || string.IsNullOrEmpty(name)
                || !diagnostic.Properties.TryGetValue(PinWithProperty, out var pinWith)
                || !root.FullSpan.Contains(diagnostic.Location.SourceSpan)
                || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration)
            {
                continue;
            }

            var action = pinWith switch
            {
                "DomainEventName" => CodeAction.Create(
                    $"Pin the name '{name}' with [DomainEventName]",
                    cancellationToken => AddDomainEventNameAsync(context.Document, declaration, name!, cancellationToken),
                    "DDDToolkit.PinDomainEventName"),
                "IntegrationEvent" => CodeAction.Create(
                    $"Pin the name '{name}' in [IntegrationEvent]",
                    cancellationToken => NameIntegrationEventAsync(context.Document, declaration, name!, cancellationToken),
                    "DDDToolkit.PinIntegrationEventName"),
                _ => null,
            };

            if (action is not null)
            {
                context.RegisterCodeFix(action, diagnostic);
            }
        }
    }

    private static async Task<Document> AddDomainEventNameAsync(Document document, TypeDeclarationSyntax declaration, string name, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // Fully qualified and annotated: the import adder puts the using in, and the code action's clean-up
        // then shortens it to [DomainEventName("...")], or leaves it qualified where the short name would
        // bind to something else.
        var attribute = Attribute(
                ParseName("global::" + AttributesNamespace + ".DomainEventName").WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation),
                AttributeArgumentList(SingletonSeparatedList(AttributeArgument(Literal(name)))));

        editor.AddAttribute(declaration, AttributeList(SingletonSeparatedList(attribute)));
        return await ImportAdder.AddImportsAsync(editor.GetChangedDocument(), Simplifier.AddImportsAnnotation, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> NameIntegrationEventAsync(Document document, TypeDeclarationSyntax declaration, string name, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var attribute = declaration.AttributeLists
            .SelectMany(static list => list.Attributes)
            .FirstOrDefault(candidate => IsIntegrationEvent(model?.GetSymbolInfo(candidate, cancellationToken).Symbol));

        if (attribute is null)
        {
            return document;
        }

        var arguments = attribute.ArgumentList?.Arguments ?? default;
        var named = AttributeArgument(Literal(name));

        // The name is the constructor's argument, so it goes before any Version = n.
        var rewritten = attribute.WithArgumentList(AttributeArgumentList(arguments.Insert(0, named)));

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(attribute, rewritten);
        return editor.GetChangedDocument();
    }

    private static bool IsIntegrationEvent(ISymbol? constructor)
        => constructor?.ContainingType is { Name: "IntegrationEventAttribute" } attributeClass
           && attributeClass.ContainingNamespace.ToDisplayString() == AttributesNamespace;

    private static LiteralExpressionSyntax Literal(string value)
        => LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value));
}
