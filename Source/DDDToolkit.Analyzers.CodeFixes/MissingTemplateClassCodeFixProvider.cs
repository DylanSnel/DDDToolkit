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
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00044 and DDD00049: a template takes a type from a class nobody declares, or a registration
/// closed over the application's classes needs one. The fix declares it, after the class the diagnostic is
/// reported on:
/// <code>
/// [TenantAggregate&lt;TenantId&gt;]
/// public sealed partial class Tenant;
/// </code>
/// <list type="bullet">
///   <item><description>The attribute is the missing template, and the class needs nothing else: the package
///   creates its instances through the constructor the generator writes.</description></item>
///   <item><description>The id is found by name, the parent's id type parameter without its <c>T</c>
///   (<c>TTenantId</c> is <c>TenantId</c>), among the entity ids of this project and then of the projects it
///   references, as the templates are. When no id has that name, the template's name with <c>Id</c> after it is
///   looked for instead (<c>OrganizationUnitId</c> for <c>[OrganizationUnit]</c>, whose parameter is <c>TUnitId</c>).
///   None, or more than one, and no fix is offered: guessing an id would bind the application's model to the
///   wrong one.</description></item>
///   <item><description>The name is the reporting class's with its template's noun replaced: <c>Seat</c>,
///   declared with <c>[SeatAggregate]</c>, gives <c>Tenant</c>, and <c>Organization</c> missing its unit gives
///   <c>OrganizationUnit</c>. A prefix is kept: <c>ShopSeat</c> gives <c>ShopTenant</c>, and
///   <c>ShopOrganization</c> gives <c>ShopUnit</c>. A name that is taken gets no fix.</description></item>
/// </list>
/// The generator works out the template, the id's name and the class's name and puts them on the diagnostic,
/// because it holds the symbols; the fix finds the id and writes the class.
/// </summary>
/// <remarks>
/// There is no fix-all. Several classes can report the same missing class, and fixing each would declare it
/// as many times, which is the next error.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingTemplateClassCodeFixProvider))]
public sealed class MissingTemplateClassCodeFixProvider : CodeFixProvider
{
    private const string SourceMissing = "DDD00044";
    private const string RegistrationSourceMissing = "DDD00049";

    // The keys the generator writes into the diagnostic, MissingTemplateClass.TemplateProperty and the rest.
    // The generator assembly is not referenced here, so they are repeated.
    private const string TemplateProperty = "Template";
    private const string IdNameProperty = "IdName";
    private const string FallbackIdNameProperty = "FallbackIdName";
    private const string ClassNameProperty = "ClassName";

    private const string AttributesNamespace = "DDDToolkit.Abstractions.Attributes";
    private const string AbstractionsAssembly = "DDDToolkit.Abstractions";

    private const string EquivalenceKey = "DDDToolkit.DeclareMissingTemplateClass";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(SourceMissing, RegistrationSourceMissing);

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(TemplateProperty, out var template) || string.IsNullOrEmpty(template)
                || !diagnostic.Properties.TryGetValue(IdNameProperty, out var idName) || string.IsNullOrEmpty(idName)
                || !diagnostic.Properties.TryGetValue(ClassNameProperty, out var className) || string.IsNullOrEmpty(className)
                || !root.FullSpan.Contains(diagnostic.Location.SourceSpan)
                || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration
                || model.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } reportedOn
                || model.Compilation.GetTypeByMetadataName(template!) is not { } attribute
                || TheOneId(model.Compilation, idName!, FallbackOf(diagnostic), context.CancellationToken) is not { } id
                || IsTaken(reportedOn, className!))
            {
                continue;
            }

            var attributeName = attribute.Name.EndsWith("Attribute", System.StringComparison.Ordinal)
                ? attribute.Name.Substring(0, attribute.Name.Length - "Attribute".Length)
                : attribute.Name;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Declare '{className}' with [{attributeName}<{id.Name}>]",
                    cancellationToken => DeclareAsync(context.Document, declaration, attribute, attributeName, id, className!, cancellationToken),
                    EquivalenceKey),
                diagnostic);
        }
    }

    private static async Task<Document> DeclareAsync(
        Document document,
        TypeDeclarationSyntax after,
        INamedTypeSymbol attribute,
        string attributeName,
        INamedTypeSymbol id,
        string className,
        CancellationToken cancellationToken)
    {
        // The new class is written the way the one before it is: its line ending, its indentation, a blank
        // line between them.
        var existing = after.DescendantTrivia().FirstOrDefault(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        var endOfLine = existing.IsKind(SyntaxKind.EndOfLineTrivia) ? existing : CarriageReturnLineFeed;
        var indentation = after.GetLeadingTrivia().Where(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Take(1).ToList();
        var separation = after.GetTrailingTrivia().Any(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            ? new[] { endOfLine }
            : new[] { endOfLine, endOfLine };

        // Fully qualified and annotated: the import adder puts in the usings, and the code action's clean-up
        // then shortens the names to [TenantAggregate<TenantId>], or leaves them qualified where the short
        // name would bind to something else.
        var name = ParseName(
                "global::" + attribute.ContainingNamespace.ToDisplayString() + "." + attributeName
                + "<" + id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">")
            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);

        var modifiers = after.Modifiers
            .Where(static modifier => modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword))
            .Select(static modifier => modifier.Kind())
            .Concat(new[] { SyntaxKind.SealedKeyword, SyntaxKind.PartialKeyword })
            .Select((kind, position) => Token(kind).WithLeadingTrivia(position == 0 ? indentation : []).WithTrailingTrivia(Space));

        // An empty class body is a semicolon from C# 12 on; before that it has to be braces.
        var semicolonBody = document.Project.ParseOptions is CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp12 };

        var declaration = ClassDeclaration(Identifier(className))
            .WithAttributeLists(SingletonList(
                AttributeList(SingletonSeparatedList(Attribute(name)))
                    .WithLeadingTrivia(separation.Concat(indentation))
                    .WithTrailingTrivia(endOfLine)))
            .WithModifiers(TokenList(modifiers))
            .WithKeyword(Token(SyntaxKind.ClassKeyword).WithTrailingTrivia(Space));

        declaration = semicolonBody
            ? declaration
                .WithOpenBraceToken(default)
                .WithCloseBraceToken(default)
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(endOfLine))
            : declaration
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(Space))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken).WithLeadingTrivia(Space).WithTrailingTrivia(endOfLine));

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.InsertAfter(after, declaration);
        return await ImportAdder.AddImportsAsync(editor.GetChangedDocument(), Simplifier.AddImportsAnnotation, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? FallbackOf(Diagnostic diagnostic)
        => diagnostic.Properties.TryGetValue(FallbackIdNameProperty, out var fallback) && !string.IsNullOrEmpty(fallback) ? fallback : null;

    /// <summary>
    /// The one entity id called <paramref name="name"/>, or, when there is none of that name, the one called
    /// <paramref name="fallback"/>. Null when there is none, or more than one: several of the first name do not
    /// fall back, because the name was right and the choice is the application's.
    /// </summary>
    private static INamedTypeSymbol? TheOneId(Compilation compilation, string name, string? fallback, CancellationToken cancellationToken)
    {
        var ids = IdsNamed(compilation, name, cancellationToken);
        if (ids.Count == 0 && fallback is not null)
        {
            ids = IdsNamed(compilation, fallback, cancellationToken);
        }

        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>
    /// The entity ids called <paramref name="name"/>: in this project, or, when it has none, in the projects it
    /// references that can declare one.
    /// </summary>
    private static List<INamedTypeSymbol> IdsNamed(Compilation compilation, string name, CancellationToken cancellationToken)
    {
        var own = compilation.GetSymbolsWithName(candidate => candidate == name, SymbolFilter.Type, cancellationToken)
            .OfType<INamedTypeSymbol>()
            .Where(IsEntityId)
            .ToList();
        if (own.Count > 0)
        {
            return own;
        }

        var referenced = new List<INamedTypeSymbol>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only an assembly that references the toolkit's attributes can declare an id with them.
            if (!assembly.Modules.Any(static module => module.ReferencedAssemblySymbols.Any(static reference => reference.Identity.Name == AbstractionsAssembly)))
            {
                continue;
            }

            referenced.AddRange(TypesIn(assembly.GlobalNamespace, name, cancellationToken)
                .Where(type => IsEntityId(type) && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)));
        }

        return referenced;
    }

    private static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceSymbol scope, string name, CancellationToken cancellationToken)
    {
        foreach (var type in scope.GetTypeMembers(name))
        {
            yield return type;
        }

        foreach (var nested in scope.GetNamespaceMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in TypesIn(nested, name, cancellationToken))
            {
                yield return type;
            }
        }
    }

    /// <summary>
    /// Whether the type is declared <c>[EntityId&lt;T&gt;]</c>. The attribute survives metadata, so it answers for an
    /// id in a referenced project as well as for one in this project, whose interfaces the generator has not
    /// written yet as far as this compilation shows.
    /// </summary>
    private static bool IsEntityId(INamedTypeSymbol type)
        => type.GetAttributes().Any(static attribute =>
            attribute.AttributeClass is { Name: "EntityIdAttribute" } attributeClass
            && attributeClass.ContainingNamespace.ToDisplayString() == AttributesNamespace);

    /// <summary>Whether a type of that name already sits next to the class the new one is declared after.</summary>
    private static bool IsTaken(INamedTypeSymbol reportedOn, string className)
        => ((INamespaceOrTypeSymbol?)reportedOn.ContainingType ?? reportedOn.ContainingNamespace).GetTypeMembers(className).Length > 0;
}
