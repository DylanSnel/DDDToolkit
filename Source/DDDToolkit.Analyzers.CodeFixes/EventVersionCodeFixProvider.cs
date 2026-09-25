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
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00034: a class name that ends in one version and an <c>[IntegrationEvent(Version = n)]</c> that
/// states another. The stated version is the one that counts, so there are two ways to make them agree:
/// <list type="number">
///   <item><description>Rename the class to the version it is, <c>OrderPlacedV2</c> to <c>OrderPlacedV3</c>. The
///   event stays what it was; only the name stops contradicting it. Offered first.</description></item>
///   <item><description>Remove <c>Version = n</c>, so the class name says it. That changes the event's version to
///   the name's, and the title says so, for the case where the name was right and the attribute was not.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// The rename is a rename of the symbol across the solution, as the IDE's own rename does it, and is not
/// offered when the containing namespace or type already declares a type of the new name. Fix-all applies
/// only to removing <c>Version</c>, which touches nothing outside the attribute.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EventVersionCodeFixProvider))]
public sealed class EventVersionCodeFixProvider : CodeFixProvider
{
    private const string VersionDisagrees = "DDD00034";

    // The key the generator writes into the diagnostic, EventNamesGenerator.RenameToProperty. The generator
    // assembly is not referenced here, so it is repeated.
    private const string RenameToProperty = "RenameTo";

    private const string RenameKey = "DDDToolkit.EventVersionRenameClass";
    private const string RemoveKey = "DDDToolkit.EventVersionFromName";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(VersionDisagrees);

    public override FixAllProvider GetFixAllProvider() => FixAll.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            if (FindVersionArgument(root, diagnostic.Location.SourceSpan) is not { } argument)
            {
                continue;
            }

            if (diagnostic.Properties.TryGetValue(RenameToProperty, out var newName) && !string.IsNullOrEmpty(newName)
                && argument.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } declaration
                && model?.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol type
                && !NameIsTaken(type, newName!))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Rename '{type.Name}' to '{newName}' to match its Version",
                        cancellationToken => RenameAsync(context.Document.Project.Solution, type, newName!, cancellationToken),
                        RenameKey),
                    diagnostic);
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove Version and use the version in the class name",
                    cancellationToken => RemoveVersionAsync(context.Document, [diagnostic], cancellationToken),
                    RemoveKey),
                diagnostic);
        }
    }

    private static Task<Solution> RenameAsync(Solution solution, INamedTypeSymbol type, string newName, CancellationToken cancellationToken)
        => Renamer.RenameSymbolAsync(solution, type, new SymbolRenameOptions(RenameFile: true), newName, cancellationToken);

    /// <summary>Whether the scope the type is declared in already has a type called <paramref name="name"/>.</summary>
    private static bool NameIsTaken(INamedTypeSymbol type, string name)
    {
        var siblings = type.ContainingType is { } container
            ? container.GetTypeMembers(name)
            : type.ContainingNamespace.GetTypeMembers(name);

        return siblings.Length > 0;
    }

    private static async Task<Document> RemoveVersionAsync(Document document, IEnumerable<Diagnostic> diagnostics, CancellationToken cancellationToken)
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
            => fixAllContext.CodeActionEquivalenceKey == RemoveKey
                ? await RemoveVersionAsync(document, diagnostics, fixAllContext.CancellationToken).ConfigureAwait(false)
                : document;
    }
}
