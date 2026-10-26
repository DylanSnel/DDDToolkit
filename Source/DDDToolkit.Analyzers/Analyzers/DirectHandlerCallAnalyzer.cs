using System;
using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DDDToolkit.Analyzers.Analyzers;

/// <summary>
/// Reports a request that says what it requires of its caller handed to its handler directly, past the pipeline
/// whose access behavior asks the checks: DDD00061, on a call to <c>Handle</c> of one of the Mediator library's
/// handlers (<c>ICommandHandler</c>, <c>IQueryHandler</c>, <c>IRequestHandler</c> and their stream kinds, or a class
/// that implements one) whose message implements an interface marked <c>[AccessRequests]</c>, and on a reference to
/// such a <c>Handle</c> that makes a delegate of it.
/// <para>
/// Nothing at run time notices such a call. The handler runs, and only what the database checks stands in its way,
/// which per table is coarser than what one request declares. So the build says it, where the call is written.
/// </para>
/// <para>
/// What it leaves alone, and why. Constructing a handler and injecting one: the call is where the check is skipped,
/// and is reported there, also through the interface a handler was injected as. Generated code: the library's own
/// dispatch calls every handler, after the pipeline. <c>base.Handle</c> in a handler that overrides it, and a handler
/// that wraps another of the same message and hands it the message it was given, unchanged: both hand on the request
/// that passed the pipeline on its way in. And a test project, one with <c>IsTestProject</c> or
/// <c>IsTestingPlatformApplication</c> set, which the toolkit's props make visible: a test that calls a handler on
/// purpose tests the handler alone, and says so by being a test. Silent where the library or the attribute cannot be
/// seen, as the generator writes no behavior there.
/// </para>
/// <para>
/// What it cannot see: a helper that calls <c>Handle</c> on a handler of a message type parameter with no constraint
/// to a marked interface, a dispatcher of a transport's own say, and the call that hands it a handler of a marked
/// request. Such a dispatcher sends through the sender, or asks the checks itself.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectHandlerCallAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The key of the diagnostic's property that names what of the sender sends the request instead: <c>Send</c>, or
    /// <c>CreateStream</c> for a query answered with a stream. The code fix reads it.
    /// </summary>
    public const string SendWithProperty = "SendWith";

    /// <summary>What Microsoft.NET.Test.Sdk sets in a test project, and what a project may set to say it is one.</summary>
    private const string TestProjectProperty = "build_property.IsTestProject";

    /// <summary>What Microsoft.Testing.Platform sets in a test application, which may lack the other.</summary>
    private const string TestingPlatformProperty = "build_property.IsTestingPlatformApplication";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.HandlerCalledDirectly];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (IsSet(options, TestProjectProperty) || IsSet(options, TestingPlatformProperty))
            {
                return;
            }

            var sender = start.Compilation.GetTypeByMetadataName(KnownTypes.MediatorSender);
            var marker = start.Compilation.GetTypeByMetadataName(KnownTypes.AccessRequestsAttribute);
            if (sender is null || marker is null)
            {
                return;
            }

            var library = sender.ContainingAssembly;
            start.RegisterOperationAction(operation => Analyze(operation, library, marker), OperationKind.Invocation, OperationKind.MethodReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, IAssemblySymbol library, INamedTypeSymbol marker)
    {
        var (method, instance) = context.Operation switch
        {
            IInvocationOperation invocation => (invocation.TargetMethod, invocation.Instance),
            IMethodReferenceOperation reference => (reference.Method, reference.Instance),
            _ => (null, null),
        };

        if (method is not { Name: "Handle", IsStatic: false }
            || instance?.Syntax is BaseExpressionSyntax
            || HandlerOf(method, library) is not { TypeArguments.Length: > 0 } handler
            || RequestsOf(handler.TypeArguments[0], marker) is not { } requests
            || (context.Operation is IInvocationOperation call && HandsOnItsOwnMessage(call, context.ContainingSymbol, handler, library)))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SendWithProperty, handler.Name.IndexOf("Stream", StringComparison.Ordinal) >= 0 ? "CreateStream" : "Send");

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HandlerCalledDirectly,
            NameOf(context.Operation.Syntax),
            properties,
            handler.TypeArguments[0].Name,
            requests.Name));
    }

    /// <summary>
    /// The handler interface of the library whose <c>Handle</c> <paramref name="method"/> is: the interface itself,
    /// called through it, or the one a class's method implements. Null for any other method called Handle.
    /// </summary>
    private static INamedTypeSymbol? HandlerOf(IMethodSymbol method, IAssemblySymbol library)
    {
        var type = method.ContainingType;
        if (IsHandler(type, library))
        {
            return type;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (!IsHandler(implemented, library))
            {
                continue;
            }

            foreach (var member in implemented.GetMembers("Handle"))
            {
                if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation && IsOrOverrides(method, implementation))
                {
                    return implemented;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="method"/> is <paramref name="implementation"/> or overrides it: a class derived from a
    /// handler that overrides <c>Handle</c> implements the interface through the method of its base, which the
    /// interface still names.
    /// </summary>
    private static bool IsOrOverrides(IMethodSymbol method, IMethodSymbol implementation)
    {
        for (var each = method; each is not null; each = each.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(each.OriginalDefinition, implementation.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="invocation"/>, in <paramref name="container"/>, is a handler handing the message it was
    /// given to another handler of the same message, unchanged: a decorator, which the library dispatches to after
    /// the pipeline, as it does to any handler. Sending it again would pass the pipeline a second time and come back
    /// to the decorator, round and round.
    /// </summary>
    private static bool HandsOnItsOwnMessage(IInvocationOperation invocation, ISymbol container, INamedTypeSymbol handler, IAssemblySymbol library)
    {
        if (container is not IMethodSymbol { Name: "Handle", IsStatic: false, Parameters.Length: > 0 } outer
            || HandlerOf(outer, library) is not { TypeArguments.Length: > 0 } outerHandler
            || !SymbolEqualityComparer.Default.Equals(outerHandler.TypeArguments[0], handler.TypeArguments[0]))
        {
            return false;
        }

        var message = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 0)?.Value;
        while (message is IConversionOperation { IsImplicit: true } conversion)
        {
            message = conversion.Operand;
        }

        return message is IParameterReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Parameter, outer.Parameters[0]);
    }

    /// <summary>
    /// Whether <paramref name="type"/> is one of the library's handlers: an interface of its assembly, in its
    /// namespace, named for a handler. Read by its shape, so a handler a later version adds is one too; its pipeline
    /// behaviors are not handlers, and a call to one of those hands the message on to the next step.
    /// </summary>
    private static bool IsHandler(INamedTypeSymbol type, IAssemblySymbol library)
        => type.TypeKind == TypeKind.Interface
           && type.Name.EndsWith("Handler", StringComparison.Ordinal)
           && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, library)
           && type.ContainingNamespace is { Name: "Mediator", ContainingNamespace.IsGlobalNamespace: true };

    /// <summary>
    /// The interface marked <c>[AccessRequests]</c> that <paramref name="message"/> is or implements, or that a type
    /// parameter is constrained to; null for a message of no module's.
    /// </summary>
    private static INamedTypeSymbol? RequestsOf(ITypeSymbol message, INamedTypeSymbol marker)
    {
        switch (message)
        {
            case INamedTypeSymbol named:
                return IsMarked(named, marker) ? named : named.AllInterfaces.FirstOrDefault(implemented => IsMarked(implemented, marker));

            case ITypeParameterSymbol parameter:
                foreach (var constraint in parameter.ConstraintTypes)
                {
                    if (RequestsOf(constraint, marker) is { } requests)
                    {
                        return requests;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static bool IsMarked(INamedTypeSymbol type, INamedTypeSymbol marker)
        => type.TypeKind == TypeKind.Interface
           && type.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker));

    /// <summary>Where the call names <c>Handle</c>: the name after the dot, where the eye looks for it.</summary>
    private static Location NameOf(SyntaxNode syntax)
        => syntax switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => access.Name.GetLocation(),
            InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } => binding.Name.GetLocation(),
            InvocationExpressionSyntax invocation => invocation.Expression.GetLocation(),
            MemberAccessExpressionSyntax access => access.Name.GetLocation(),
            _ => syntax.GetLocation(),
        };

    private static bool IsSet(AnalyzerConfigOptions options, string key)
        => options.TryGetValue(key, out var value) && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
