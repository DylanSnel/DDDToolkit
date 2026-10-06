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
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00067: a class whose package makes its new ids is declared over an id that cannot make one with
/// <c>Create()</c>. The fix adds what the id lacks to its own declaration, wherever in the solution that is, the
/// class's project or its contracts project. For an <c>[EntityId&lt;T&gt;]</c>, which the generator completes with
/// the interface, that is a <c>Create()</c>:
/// <code>
/// [EntityId&lt;long&gt;]
/// public readonly partial record struct TenantId
/// {
///     /// &lt;summary&gt;A new TenantId, made in code before the save.&lt;/summary&gt;
///     public static TenantId Create() =&gt; throw new NotImplementedException("Make a new TenantId in code: ...");
/// }
/// </code>
/// An id written by hand, which no generator completes, gets <c>ICreatableEntityId&lt;TId&gt;</c> beside it, or only
/// the interface when it has a fitting <c>Create()</c> already.
/// <para>
/// It cannot know how the application makes a new id, a snowflake or a number of a HiLo block for a <c>long</c>, so
/// the body throws, saying what to write: the build is green again, and the first id the package makes says where
/// the body is missing. The generator writes the id's metadata name, which fix fits and the example of a new value
/// into the diagnostic, because it holds the symbol; the fix finds the declaration. No fix is offered where what is
/// missing is not in a declaration: an id with no source in the solution, one of a package, a <c>Create()</c> that
/// does not fit, which is the author's to change, or an id whose project cannot see the interface.
/// </para>
/// </summary>
/// <remarks>
/// There is no fix-all: several classes over one id each report it, and a second <c>Create()</c> is the next error.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCreateCodeFixProvider))]
public sealed class MissingCreateCodeFixProvider : CodeFixProvider
{
    private const string IdWithoutCreate = "DDD00067";

    // The keys and values the generator writes into the diagnostic (IdShortfall in the generator's shared code).
    // The generator assembly is not referenced here, so they are repeated.
    private const string IdMetadataNameProperty = "IdMetadataName";
    private const string FixProperty = "IdFix";
    private const string ExampleProperty = "IdExample";
    private const string AddCreate = "Create";
    private const string AddCreateAndInterface = "CreateAndInterface";
    private const string AddInterface = "Interface";

    private const string CreatableInterface = "DDDToolkit.Abstractions.Interfaces.ICreatableEntityId`1";

    private const string EquivalenceKey = "DDDToolkit.AddCreateToTheId";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(IdWithoutCreate);

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var compilation = await context.Document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            return;
        }

        var solution = context.Document.Project.Solution;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(IdMetadataNameProperty, out var metadataName)
                || string.IsNullOrEmpty(metadataName)
                || compilation.GetTypeByMetadataName(metadataName!) is not { } id
                || !diagnostic.Properties.TryGetValue(FixProperty, out var fix)
                || fix is not (AddCreate or AddCreateAndInterface or AddInterface))
            {
                continue;
            }

            // The id as its own project declares it: this one, or another project of the solution it references.
            var declared = id.DeclaringSyntaxReferences.IsEmpty
                ? await SymbolFinder.FindSourceDefinitionAsync(id, solution, context.CancellationToken).ConfigureAwait(false) as INamedTypeSymbol
                : id;
            if (declared?.DeclaringSyntaxReferences.FirstOrDefault() is not { } reference
                || solution.GetDocument(reference.SyntaxTree) is not { } document)
            {
                continue;
            }

            // A second Create() would be the next error, and an interface its project cannot see one more.
            var addsCreate = fix != AddInterface;
            var addsInterface = fix != AddCreate;
            if (addsCreate && declared.GetMembers("Create").OfType<IMethodSymbol>().Any(static method => method.Parameters.IsEmpty && method.TypeParameters.IsEmpty))
            {
                continue;
            }

            if (addsInterface
                && (await document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false))?.GetTypeByMetadataName(CreatableInterface) is null)
            {
                continue;
            }

            var example = diagnostic.Properties.TryGetValue(ExampleProperty, out var given) && !string.IsNullOrEmpty(given)
                ? given!
                : "a value no other row has";
            var interfaceName = "ICreatableEntityId<" + id.Name + ">";
            var title = fix switch
            {
                AddCreate => $"Add a Create() to '{id.Name}'",
                AddCreateAndInterface => $"Add a Create() and {interfaceName} to '{id.Name}'",
                _ => $"Add {interfaceName} to '{id.Name}'",
            };

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => AddAsync(document, reference.Span, id.Name, addsCreate ? example : null, addsInterface, cancellationToken),
                    EquivalenceKey + "." + fix),
                diagnostic);
        }
    }

    /// <summary>
    /// Adds a throwing <c>Create()</c> when <paramref name="example"/> is given, which says what to make a new id of,
    /// and <c>ICreatableEntityId&lt;TId&gt;</c> to the interfaces when <paramref name="addsInterface"/>.
    /// </summary>
    private static async Task<Solution> AddAsync(
        Document document,
        Microsoft.CodeAnalysis.Text.TextSpan span,
        string name,
        string? example,
        bool addsInterface,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root?.FindNode(span).FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration)
        {
            return document.Project.Solution;
        }

        var changed = declaration;
        if (addsInterface)
        {
            // Fully qualified and annotated: the import adder puts in the using, and the code action's clean-up then
            // shortens the name, or leaves it qualified where the short name would bind to something else.
            var creatable = ParseTypeName("global::DDDToolkit.Abstractions.Interfaces.ICreatableEntityId<" + name + ">")
                .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);
            changed = (TypeDeclarationSyntax)changed.AddBaseListTypes(SimpleBaseType(creatable));
        }

        if (example is not null)
        {
            // Written with the line ending the file has, and apart from the members the id has already by a blank line.
            var existing = root.DescendantTrivia().FirstOrDefault(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            var endOfLine = existing.IsKind(SyntaxKind.EndOfLineTrivia) ? existing : CarriageReturnLineFeed;
            var newLine = endOfLine.ToFullString();
            var member = (MemberDeclarationSyntax)ParseMemberDeclaration(
                    (declaration.Members.Count > 0 ? newLine : string.Empty)
                    + "/// <summary>A new " + name + ", made in code before the save.</summary>" + newLine
                    + "public static " + name + " Create() => throw new global::System.NotImplementedException(\"Make a new " + name
                    + " in code: " + example.Replace("\\", "\\\\").Replace("\"", "\\\"") + ".\");" + newLine)!
                .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);

            // An id declared with a semicolon, as most are, gets the braces a member needs; what followed the
            // semicolon follows the closing brace.
            if (changed.SemicolonToken.IsKind(SyntaxKind.SemicolonToken))
            {
                changed = changed
                    .WithSemicolonToken(default)
                    .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                    .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken).WithTrailingTrivia(declaration.SemicolonToken.TrailingTrivia));
            }

            changed = changed.AddMembers(member);
        }

        var withChange = document.WithSyntaxRoot(root.ReplaceNode(declaration, changed.WithAdditionalAnnotations(Formatter.Annotation)));
        return (await ImportAdder.AddImportsAsync(withChange, Simplifier.AddImportsAnnotation, cancellationToken: cancellationToken).ConfigureAwait(false)).Project.Solution;
    }
}
