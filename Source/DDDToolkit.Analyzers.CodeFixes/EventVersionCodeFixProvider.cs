using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00034: a class name that ends in a version and an <c>[IntegrationEvent(Version = n)]</c> that says
/// another one. The fix removes <c>Version = n</c>, so the name is the only place the version is written:
/// <c>[IntegrationEvent(Version = 3)]</c> on <c>OrderPlacedV2</c> becomes <c>[IntegrationEvent]</c>.
/// </summary>
/// <remarks>
/// Renaming the class is the other way out, and the one to take when the attribute was right and the name
/// was not. That is a rename across the solution, which the IDE's own rename does better than a fix could.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EventVersionCodeFixProvider))]
public sealed class EventVersionCodeFixProvider : CodeFixProvider
{
    private const string VersionDisagrees = "DDD00034";

    private const string EquivalenceKey = "DDDToolkit.EventVersionFromName";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(VersionDisagrees);

    public override FixAllProvider GetFixAllProvider() => FixAll.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (FindVersionArgument(root, diagnostic.Location.SourceSpan) is not null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create("Remove Version and keep the one in the class name", cancellationToken => FixAsync(context.Document, [diagnostic], cancellationToken), EquivalenceKey),
                    diagnostic);
            }
        }
    }

    private static async Task<Document> FixAsync(Document document, IEnumerable<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var attributes = diagnostics
            .Select(diagnostic => FindVersionArgument(editor.OriginalRoot, diagnostic.Location.SourceSpan))
            .OfType<AttributeArgumentSyntax>()
            .Select(static argument => (AttributeSyntax)argument.Parent!.Parent!)
            .Distinct();

        foreach (var attribute in attributes)
        {
            editor.ReplaceNode(attribute, (current, _) => WithoutVersion((AttributeSyntax)current));
        }

        return editor.GetChangedDocument();
    }

    /// <summary>The <c>Version = n</c> argument a diagnostic points at, or null when it points elsewhere.</summary>
    private static AttributeArgumentSyntax? FindVersionArgument(SyntaxNode root, TextSpan span)
    {
        if (!root.FullSpan.Contains(span))
        {
            return null;
        }

        return root.FindNode(span, getInnermostNodeForTie: true).FirstAncestorOrSelf<AttributeArgumentSyntax>() is { NameEquals.Name.Identifier.ValueText: "Version", Parent.Parent: AttributeSyntax } argument
            ? argument
            : null;
    }

    private static AttributeSyntax WithoutVersion(AttributeSyntax attribute)
    {
        var arguments = attribute.ArgumentList!.Arguments;
        var version = arguments.FirstOrDefault(static argument => argument.NameEquals?.Name.Identifier.ValueText == "Version");
        if (version is null)
        {
            return attribute;
        }

        var remaining = arguments.Remove(version);

        // [IntegrationEvent(Version = 3)] becomes [IntegrationEvent], not [IntegrationEvent()].
        return remaining.Count == 0
            ? attribute.WithArgumentList(null).WithTriviaFrom(attribute)
            : attribute.WithArgumentList(attribute.ArgumentList.WithArguments(remaining));
    }

    private sealed class FixAll : DocumentBasedFixAllProvider
    {
        public static FixAll Instance { get; } = new();

        protected override async Task<Document?> FixAllAsync(FixAllContext fixAllContext, Document document, ImmutableArray<Diagnostic> diagnostics)
            => await FixAsync(document, diagnostics, fixAllContext.CancellationToken).ConfigureAwait(false);
    }
}
