using System.Collections.Generic;
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
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00010 and DDD00011: a value object property whose setter is not <c>protected init</c>. The
/// fix rewrites the setter, <c>{ get; set; }</c> to <c>{ get; protected init; }</c>.
/// </summary>
/// <remarks>
/// A positional record never reports either diagnostic: the generator declares its properties as
/// <c>protected init</c> itself, so there is nothing here for it.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ProtectedInitCodeFixProvider))]
public sealed class ProtectedInitCodeFixProvider : CodeFixProvider
{
    private const string UseProtectedSetters = "DDD00010";
    private const string UseInitSetters = "DDD00011";

    private const string EquivalenceKey = "DDDToolkit.ProtectedInit";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(UseProtectedSetters, UseInitSetters);

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
            if (FindProperty(root, diagnostic.Location.SourceSpan) is not null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create("Use 'protected init'", cancellationToken => FixAsync(context.Document, [diagnostic], cancellationToken), EquivalenceKey),
                    diagnostic);
            }
        }
    }

    private static async Task<Document> FixAsync(Document document, IEnumerable<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var properties = diagnostics
            .Select(diagnostic => FindProperty(editor.OriginalRoot, diagnostic.Location.SourceSpan))
            .OfType<PropertyDeclarationSyntax>()
            .Distinct();

        foreach (var property in properties)
        {
            editor.ReplaceNode(property, (current, _) => RewriteSetter((PropertyDeclarationSyntax)current));
        }

        return editor.GetChangedDocument();
    }

    /// <summary>The property a diagnostic points at, or <see langword="null"/> when the fix cannot help it.</summary>
    private static PropertyDeclarationSyntax? FindProperty(SyntaxNode root, TextSpan span)
    {
        if (!root.FullSpan.Contains(span))
        {
            return null;
        }

        return root.FindNode(span, getInnermostNodeForTie: true).FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } property
            && Setter(property) is not null
            && SetterModifiers(property) is not null
                ? property
                : null;
    }

    private static AccessorDeclarationSyntax? Setter(PropertyDeclarationSyntax property)
        => property.AccessorList?.Accessors.FirstOrDefault(accessor =>
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration));

    /// <summary>
    /// The accessor modifiers that make the setter protected, which depend on the property's own
    /// accessibility: an accessor must be more restrictive than its property, so a <c>protected</c>
    /// property needs none and an <c>internal</c> one needs <c>private protected</c>. A private property
    /// cannot have a protected setter at all, so there is no fix for it.
    /// </summary>
    private static SyntaxKind[]? SetterModifiers(PropertyDeclarationSyntax property)
    {
        var modifiers = property.Modifiers;

        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return modifiers.Any(SyntaxKind.InternalKeyword) ? [SyntaxKind.ProtectedKeyword] : [];
        }

        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return [SyntaxKind.ProtectedKeyword];
        }

        if (modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return [SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword];
        }

        return null;
    }

    private static PropertyDeclarationSyntax RewriteSetter(PropertyDeclarationSyntax property)
    {
        var setter = Setter(property)!;
        var modifiers = TokenList(SetterModifiers(property)!.Select(kind => Token(kind).WithTrailingTrivia(Space)));

        var rewritten = setter
            .WithModifiers(modifiers)
            .WithKeyword(Token(SyntaxKind.InitKeyword).WithTrailingTrivia(setter.Keyword.TrailingTrivia))
            .WithLeadingTrivia(setter.GetLeadingTrivia());

        return property.ReplaceNode(setter, rewritten);
    }

    private sealed class FixAll : DocumentBasedFixAllProvider
    {
        public static FixAll Instance { get; } = new();

        protected override async Task<Document?> FixAllAsync(FixAllContext fixAllContext, Document document, ImmutableArray<Diagnostic> diagnostics)
            => await FixAsync(document, diagnostics, fixAllContext.CancellationToken).ConfigureAwait(false);
    }
}
