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
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DDDToolkit.Analyzers.CodeFixes;

/// <summary>
/// Fixes DDD00061: a request that declares what it requires of its caller, handed to its handler directly. The fix
/// sends it instead, through a sender the code can already reach, so it passes the pipeline and the access behavior
/// in it:
/// <code>
/// await handler.Handle(new CloseInvoice(invoice), cancellationToken);   // before
/// await sender.Send(new CloseInvoice(invoice), cancellationToken);      // after
/// </code>
/// A query answered with a stream is sent with <c>CreateStream</c>. The arguments stay as they are, without names,
/// since the sender's parameters are named otherwise, and in the order of the handler's parameters, which named
/// arguments need not be written in; both take the request and then the cancellation token, and answer what the
/// handler answers.
/// </summary>
/// <remarks>
/// The sender is the Mediator library's <c>ISender</c>, or anything that is one, <c>IMediator</c> say, held by a local
/// declared before the call, a parameter, or a field or property of the class, in that order of preference, and by
/// name within each. Only one the call can use counts: a static member reaches no field, property or parameter of
/// the primary constructor, and a static lambda or local function reaches no local or parameter of the code around
/// it either. Where the code reaches none, no fix is offered: injecting one changes a constructor, and the container
/// that calls it, which is the developer's to decide. A reference to <c>Handle</c> that is no call has no fix either.
/// The handler the call used is left where it is, for the developer to remove once nothing uses it.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SendThroughSenderCodeFixProvider))]
public sealed class SendThroughSenderCodeFixProvider : CodeFixProvider
{
    private const string HandlerCalledDirectly = "DDD00061";

    // The key the analyzer writes into the diagnostic, DirectHandlerCallAnalyzer.SendWithProperty. The analyzer
    // assembly is not referenced here, so it is repeated.
    private const string SendWithProperty = "SendWith";

    private const string Sender = "Mediator.ISender";

    private const string EquivalenceKey = "DDDToolkit.SendThroughSender";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(HandlerCalledDirectly);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null || model.Compilation.GetTypeByMetadataName(Sender) is not { } sender)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!root.FullSpan.Contains(diagnostic.Location.SourceSpan)
                || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { Expression: MemberAccessExpressionSyntax access } invocation
                || access.Name.Span != diagnostic.Location.SourceSpan
                || InParameterOrder(model, invocation, context.CancellationToken) is not { } arguments
                || SenderInReach(model, invocation, sender, context.CancellationToken) is not { } reached)
            {
                continue;
            }

            var sendWith = diagnostic.Properties.TryGetValue(SendWithProperty, out var named) && !string.IsNullOrEmpty(named) ? named! : "Send";
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Send it with {reached}.{sendWith}",
                    cancellationToken => SendAsync(context.Document, invocation, arguments, reached, sendWith, cancellationToken),
                    EquivalenceKey),
                diagnostic);
        }
    }

    private static async Task<Document> SendAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        ImmutableArray<ArgumentSyntax> arguments,
        string sender,
        string sendWith,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var access = (MemberAccessExpressionSyntax)invocation.Expression;
        var sent = invocation
            .WithExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(Escaped(sender)), IdentifierName(sendWith)).WithTriviaFrom(access))
            .WithArgumentList(invocation.ArgumentList.WithArguments(SeparatedList(
                arguments.Select(static argument => argument.WithNameColon(null)),
                invocation.ArgumentList.Arguments.GetSeparators())));

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, sent));
    }

    /// <summary>
    /// The arguments of <paramref name="invocation"/> in the order of the parameters they are for, as the sender
    /// takes them once their names are gone; null where that cannot be told, which offers no fix.
    /// </summary>
    private static ImmutableArray<ArgumentSyntax>? InParameterOrder(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var written = invocation.ArgumentList.Arguments;
        if (!written.Any(static argument => argument.NameColon is not null))
        {
            return written.ToImmutableArray();
        }

        if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
        {
            return null;
        }

        var ordinals = new Dictionary<ArgumentSyntax, int>();
        foreach (var argument in operation.Arguments)
        {
            if (argument.Syntax is ArgumentSyntax syntax && argument.Parameter is { } parameter)
            {
                ordinals[syntax] = parameter.Ordinal;
            }
        }

        return written.All(ordinals.ContainsKey)
            ? written.OrderBy(argument => ordinals[argument]).ToImmutableArray()
            : null;
    }

    /// <summary>
    /// The name of a sender the code reaches at the call: a local declared before it, a parameter, then a field or a
    /// property, each only where the call can use it. Null for none.
    /// </summary>
    private static string? SenderInReach(SemanticModel model, InvocationExpressionSyntax invocation, INamedTypeSymbol sender, CancellationToken cancellationToken)
    {
        var position = invocation.SpanStart;

        // A lambda or a local function is in the member it is written in. A static one reaches nothing of the
        // instance, and no local or parameter of the code around it: only its own, and those of what it holds.
        var member = model.GetEnclosingSymbol(position, cancellationToken);
        var reachable = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var walledIn = false;
        while (member is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction } nested)
        {
            if (!walledIn)
            {
                reachable.Add(nested);
            }

            walledIn |= nested.IsStatic;
            member = nested.ContainingSymbol;
        }

        if (member is not null && !walledIn)
        {
            reachable.Add(member);
        }

        var inStatic = walledIn || (member?.IsStatic ?? false);

        return model.LookupSymbols(position)
            .Select(symbol => (Symbol: symbol, Rank: RankOf(symbol, position, reachable, inStatic, sender)))
            .Where(static candidate => candidate.Rank >= 0)
            .OrderBy(static candidate => candidate.Rank)
            .ThenBy(static candidate => candidate.Symbol.Name, StringComparer.Ordinal)
            .Select(static candidate => candidate.Symbol.Name)
            .FirstOrDefault();
    }

    /// <summary>
    /// How much a symbol is preferred as the sender, lower first; -1 for one that is no sender here, or one the call
    /// cannot use: a local or a parameter of a method outside <paramref name="reachable"/>, or, from static code, a
    /// field, a property or a parameter of the primary constructor.
    /// </summary>
    private static int RankOf(ISymbol symbol, int position, HashSet<ISymbol> reachable, bool inStatic, INamedTypeSymbol sender)
    {
        var (type, rank) = symbol switch
        {
            // Locals are in scope through their whole block, the part before their declaration too.
            ILocalSymbol local when reachable.Contains(local.ContainingSymbol) && local.DeclaringSyntaxReferences.All(reference => reference.Span.End < position) => (local.Type, 0),
            IParameterSymbol parameter when reachable.Contains(parameter.ContainingSymbol) => (parameter.Type, 1),
            IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } } primary when !inStatic => (primary.Type, 1),
            IFieldSymbol field when !inStatic || field.IsStatic => (field.Type, 2),
            IPropertySymbol property when (!inStatic || property.IsStatic) && property.GetMethod is not null => (property.Type, 2),
            _ => ((ITypeSymbol?)null, -1),
        };

        return type is not null && IsSender(type, sender) ? rank : -1;
    }

    /// <summary>A name as code writes it: with an <c>@</c> when it is a keyword.</summary>
    private static string Escaped(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static bool IsSender(ITypeSymbol type, INamedTypeSymbol sender)
        => SymbolEqualityComparer.Default.Equals(type, sender) || type.AllInterfaces.Contains(sender, SymbolEqualityComparer.Default);
}
