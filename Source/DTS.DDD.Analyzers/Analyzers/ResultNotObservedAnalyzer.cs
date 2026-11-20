using System.Collections.Immutable;
using DTS.DDD.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DTS.DDD.Analyzers.Analyzers;

/// <summary>
/// DDD00111: a call that returns a <c>Result</c> or <c>Result&lt;T&gt;</c>, directly or awaited, stands as a statement
/// of its own, so its failure is lost. A discard (<c>_ = Call()</c>) is no statement of that kind and is not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultNotObservedAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.ResultNotObserved];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var result = start.Compilation.GetTypeByMetadataName(KnownTypes.Result);
            var resultOfT = start.Compilation.GetTypeByMetadataName(KnownTypes.ResultOfT);
            if (result is null || resultOfT is null)
            {
                return;
            }

            start.RegisterOperationAction(operation => Analyze(operation, result, resultOfT), OperationKind.ExpressionStatement);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol result, INamedTypeSymbol resultOfT)
    {
        var statement = (IExpressionStatementOperation)context.Operation;
        var (invocation, type) = statement.Operation switch
        {
            IInvocationOperation called => (called, called.Type),
            IAwaitOperation { Operation: IInvocationOperation awaited } wait => (awaited, wait.Type),
            _ => (null, null),
        };

        // Tap and TapError run for their side effect and return the result they were called on: no new failure is lost.
        if (invocation is null || !IsResult(type, result, resultOfT) || IsResult(invocation.TargetMethod.ContainingType, result, resultOfT))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ResultNotObserved, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
    }

    private static bool IsResult(ITypeSymbol? type, INamedTypeSymbol result, INamedTypeSymbol resultOfT)
        => type is INamedTypeSymbol named
           && (SymbolEqualityComparer.Default.Equals(named, result) || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, resultOfT));
}