using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DTS.DDD.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DTS.DDD.Analyzers.Analyzers;

/// <summary>
/// Reports DDD00107 on a call to the registration of an access behavior that the compiler cannot find, as in
/// <c>services.AddBillingAccessBehavior()</c>. The compiler reports CS1061 at that call, and CS1061 does not say what is
/// missing.
/// <para>
/// The message names one of two causes. The registration is declared in the namespace of the interface, so code outside
/// that namespace needs a using directive. Or the project of the interface was built where the toolkit's mediator is not,
/// for netstandard2.0 say, so the generator wrote no behavior.
/// </para>
/// <para>
/// The diagnostic is on the call and not on the interface. A project without a behavior may ask
/// <c>AccessChecks&lt;TRequests&gt;.RequireAsync</c> itself. Such a project writes no call, so nothing is reported there.
/// </para>
/// <para>
/// In a project that sees the mediator, an interface without a behavior is DDD00056 or DDD00057. Both name the cause on
/// the interface, so this analyzer adds nothing to them. It looks through the referenced assemblies only after a call
/// failed to bind, so a build that compiles does not pay for the search.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AccessBehaviorCallAnalyzer : DiagnosticAnalyzer
{
    private const string RegistrationPrefix = "Add";
    private const string BehaviorSuffix = "AccessBehavior";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.AccessBehaviorRegistrationNotFound];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var marker = start.Compilation.GetTypeByMetadataName(KnownTypes.AccessRequestsAttribute);
            var services = start.Compilation.GetTypeByMetadataName(KnownTypes.ServiceCollection);
            if (marker is null || services is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(node => Analyze(node, marker, services), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol marker, INamedTypeSymbol services)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: var called } } access
            || !called.StartsWith(RegistrationPrefix, StringComparison.Ordinal)
            || !called.EndsWith(BehaviorSuffix, StringComparison.Ordinal)
            || context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken) is not { Symbol: null, CandidateSymbols.IsEmpty: true }
            || context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type is not { } receiver
            || !IsServiceCollection(receiver, services))
        {
            return;
        }

        var behavior = called.Substring(RegistrationPrefix.Length);
        foreach (var requests in MarkedInterfaces(context.Compilation.GlobalNamespace, marker))
        {
            if (AccessRequestsInterface.NamedAfter(requests.Name) + BehaviorSuffix == behavior
                && context.Compilation.IsSymbolAccessibleWithin(requests, context.Compilation.Assembly)
                && WhyNotFound(requests, behavior, context.Compilation) is { } reason)
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.AccessBehaviorRegistrationNotFound, access.Name.GetLocation(), called, requests.Name, reason));
            }
        }
    }

    /// <summary>
    /// What the call misses of <paramref name="requests"/>, or <see langword="null"/> where another diagnostic says it.
    /// A referenced project compiled, so it has neither DDD00056 nor DDD00057, and only a build without the mediator
    /// leaves it without a behavior.
    /// </summary>
    private static string? WhyNotFound(INamedTypeSymbol requests, string behavior, Compilation compilation)
    {
        var declaredIn = requests.ContainingNamespace;
        if (declaredIn.GetTypeMembers(behavior + "Registration").Any(registration => registration.GetMembers(RegistrationPrefix + behavior).Length > 0))
        {
            return "It is declared in the namespace '" + declaredIn.ToDisplayString() + "', which this file does not import. Add 'using " + declaredIn.ToDisplayString() + ";'";
        }

        var inThisProject = SymbolEqualityComparer.Default.Equals(requests.ContainingAssembly, compilation.Assembly);
        if (declaredIn.GetTypeMembers(behavior, 2).Length > 0
            || (inThisProject && compilation.GetTypeByMetadataName(KnownTypes.MediatorPipelineBehavior) is not null))
        {
            return null;
        }

        var project = requests.ContainingAssembly.Name;
        return "The generator writes it only in a project that sees the toolkit's mediator, which DylansTechStack.DDD.Abstractions declares for .NET 10, and '" + project + "' has no behavior. Build '" + project + "' for .NET 10 with DylansTechStack.DDD, or remove this call and ask AccessChecks<" + requests.Name + ">.RequireAsync yourself";
    }

    private static bool IsServiceCollection(ITypeSymbol type, INamedTypeSymbol services)
        => SymbolEqualityComparer.Default.Equals(type, services) || type.AllInterfaces.Contains(services, SymbolEqualityComparer.Default);

    /// <summary>The interfaces marked <c>[AccessRequests]</c> at namespace level, in this project and in what it references.</summary>
    private static IEnumerable<INamedTypeSymbol> MarkedInterfaces(INamespaceSymbol declaredIn, INamedTypeSymbol marker)
    {
        foreach (var type in declaredIn.GetTypeMembers())
        {
            if (type.TypeKind == TypeKind.Interface
                && type.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)))
            {
                yield return type;
            }
        }

        foreach (var inner in declaredIn.GetNamespaceMembers())
        {
            foreach (var type in MarkedInterfaces(inner, marker))
            {
                yield return type;
            }
        }
    }
}
