using System;
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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00074: a design-time factory whose context does not keep its migration history where the application
/// does. The fix adds <c>UseDDDToolkitDesignTime()</c> to the options the factory hands its context, in front of the
/// <c>Options</c> it reads from them, and the <c>using</c> it needs:
/// <code>
/// => new(new DbContextOptionsBuilder&lt;OrderingContext&gt;().UseNpgsql("Host=unused").Options);                              // before
/// => new(new DbContextOptionsBuilder&lt;OrderingContext&gt;().UseNpgsql("Host=unused").UseDDDToolkitDesignTime().Options);   // after
/// </code>
/// A chain written over several lines gets the call on a line of its own, indented as the line after it. Every
/// <c>Options</c> of a builder in <c>CreateDbContext</c> gets it, which is one in a factory that makes one context;
/// the call adds nothing options have already, so a second is harmless.
/// </summary>
/// <remarks>
/// No fix is offered where <c>CreateDbContext</c> reads no <c>Options</c> of a builder, a factory whose options a
/// helper of another class makes and hands back finished: where the call goes there is the developer's to decide.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DesignTimeFactoryCodeFixProvider))]
public sealed class DesignTimeFactoryCodeFixProvider : CodeFixProvider
{
    private const string FactoryWithoutTheToolkit = "DDD00074";

    private const string OptionsBuilder = "Microsoft.EntityFrameworkCore.DbContextOptionsBuilder";

    private const string ToolkitNamespace = "DDDToolkit.EntityFramework";

    private const string Call = "UseDDDToolkitDesignTime";

    private const string EquivalenceKey = "DDDToolkit.UseDDDToolkitDesignTime";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(FactoryWithoutTheToolkit);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null || model.Compilation.GetTypeByMetadataName(OptionsBuilder) is not { } builder)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!root.FullSpan.Contains(diagnostic.Location.SourceSpan)
                || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } create
                || OptionsRead(model, create, builder, context.CancellationToken).Count == 0)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add .UseDDDToolkitDesignTime() to the context's options",
                    cancellationToken => AddAsync(context.Document, create, cancellationToken),
                    EquivalenceKey),
                diagnostic);
        }
    }

    private static async Task<Document> AddAsync(Document document, MethodDeclarationSyntax create, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null || model.Compilation.GetTypeByMetadataName(OptionsBuilder) is not { } builder)
        {
            return document;
        }

        var reads = OptionsRead(model, create, builder, cancellationToken);
        if (reads.Count == 0)
        {
            return document;
        }

        // Whether the call is in reach where it goes, before the edit: a using of the namespace, a global one among them.
        var inReach = reads.All(read => model.GetTypeInfo(read.Expression, cancellationToken).Type is { } receiver
            && model.LookupSymbols(read.Expression.SpanStart, receiver, Call, includeReducedExtensionMethods: true).Any());

        var changed = root.ReplaceNodes(reads, static (_, read) => read.WithExpression(WithTheCall(read)));
        if (!inReach && changed is CompilationUnitSyntax unit)
        {
            changed = WithUsing(unit);
        }

        return document.WithSyntaxRoot(changed);
    }

    /// <summary>
    /// The builder <paramref name="read"/> reads <c>Options</c> from, with the call after it: on the line the
    /// <c>.Options</c> is on, or on a line of its own in front of it where the chain is written over several lines.
    /// </summary>
    private static ExpressionSyntax WithTheCall(MemberAccessExpressionSyntax read)
    {
        var options = read.Expression;
        var dot = Token(SyntaxKind.DotToken).WithLeadingTrivia(read.OperatorToken.LeadingTrivia);

        return InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, options, dot, IdentifierName(Call)), ArgumentList())
            .WithTrailingTrivia(options.GetTrailingTrivia());
    }

    /// <summary>
    /// Every <c>.Options</c> in <paramref name="create"/> read from a <c>DbContextOptionsBuilder</c>, the generic one
    /// or another derived from it.
    /// </summary>
    private static List<MemberAccessExpressionSyntax> OptionsRead(SemanticModel model, MethodDeclarationSyntax create, INamedTypeSymbol builder, CancellationToken cancellationToken)
    {
        var body = (SyntaxNode?)create.Body ?? create.ExpressionBody;
        if (body is null)
        {
            return [];
        }

        return body.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(access => access.Name.Identifier.ValueText == "Options"
                && model.GetTypeInfo(access.Expression, cancellationToken).Type is INamedTypeSymbol type
                && IsBuilder(type, builder))
            .ToList();
    }

    private static bool IsBuilder(INamedTypeSymbol type, INamedTypeSymbol builder)
    {
        for (var each = (INamedTypeSymbol?)type; each is not null; each = each.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(each, builder))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="unit"/> with <c>using DDDToolkit.EntityFramework;</c> among its usings, where the sorted order
    /// puts it (<see cref="ComesAfterTheToolkit"/>), with the line ending the file uses.
    /// </summary>
    private static CompilationUnitSyntax WithUsing(CompilationUnitSyntax unit)
    {
        if (unit.Usings.Any(existing => existing.Alias is null && existing.StaticKeyword.IsKind(SyntaxKind.None) && existing.Name?.ToString() == ToolkitNamespace))
        {
            return unit;
        }

        var newLine = unit.ToFullString().Contains("\r\n") ? CarriageReturnLineFeed : LineFeed;
        var directive = UsingDirective(ParseName(ToolkitNamespace)).NormalizeWhitespace().WithTrailingTrivia(newLine);

        var plain = unit.Usings
            .Select((existing, index) => (Existing: existing, Index: index))
            .Where(each => each.Existing.Alias is null && each.Existing.StaticKeyword.IsKind(SyntaxKind.None) && each.Existing.GlobalKeyword.IsKind(SyntaxKind.None))
            .ToList();
        var before = plain.FirstOrDefault(each => ComesAfterTheToolkit(each.Existing.Name?.ToString() ?? string.Empty));
        var at = before.Existing is not null ? before.Index : plain.Count > 0 ? plain[plain.Count - 1].Index + 1 : 0;

        if (at == 0 && unit.Usings.Count > 0)
        {
            // In front of the first using: the comments and blank lines before it stay at the top of the file.
            var first = unit.Usings[0];
            directive = directive.WithLeadingTrivia(first.GetLeadingTrivia());
            return unit.WithUsings(unit.Usings.Replace(first, first.WithLeadingTrivia()).Insert(0, directive));
        }

        if (unit.Usings.Count == 0)
        {
            // The first using of the file, with a blank line between it and what follows.
            var leading = unit.GetLeadingTrivia();
            var rest = unit.WithoutLeadingTrivia();
            return rest.WithUsings(rest.Usings.Add(directive.WithLeadingTrivia(leading).WithTrailingTrivia(newLine, newLine)));
        }

        return unit.WithUsings(unit.Usings.Insert(at, directive));
    }

    /// <summary>
    /// Whether a using of <paramref name="name"/> goes after the toolkit's in sorted order: the <c>System</c> namespaces
    /// first, as most files write them, and the rest by name.
    /// </summary>
    private static bool ComesAfterTheToolkit(string name)
        => !IsSystem(name) && string.Compare(name, ToolkitNamespace, StringComparison.OrdinalIgnoreCase) > 0;

    private static bool IsSystem(string name) => name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
}
