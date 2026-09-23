using System.Collections.Immutable;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DDDToolkit.HotChocolate.Analyzers;

/// <summary>
/// Reports <c>AddNodeIdValueSerializerFrom&lt;TId&gt;()</c> called with a toolkit identifier (DDD00032).
/// </summary>
/// <remarks>
/// HotChocolate's generator intercepts that call and writes a serializer from the properties the type
/// declares in source. An identifier's <c>Value</c> comes from the toolkit's generator, which it cannot
/// see, so the serializer it writes stores nothing and reads every node id back as an empty id. It
/// compiles, it runs, and every node id is <c>Order:</c>. The generated runtime bindings already register
/// a serializer that works, so the call is at best redundant and at worst replaces the working one.
/// <para>
/// An analyzer rather than a generator diagnostic: it has to see the call, which is in the user's code,
/// and the identifier, which may be generated. Analyzers run after the generators, so both are there.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NodeIdSerializerFromAnalyzer : DiagnosticAnalyzer
{
    private const string MethodName = "AddNodeIdValueSerializerFrom";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.NodeIdSerializerFromToolkitId,
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name != MethodName || method.TypeArguments.Length != 1)
        {
            return;
        }

        if (method.TypeArguments[0] is not INamedTypeSymbol id || !DefinitionFactory.IsEntityId(id))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.NodeIdSerializerFromToolkitId,
            invocation.Syntax.GetLocation(),
            id.Name));
    }
}
