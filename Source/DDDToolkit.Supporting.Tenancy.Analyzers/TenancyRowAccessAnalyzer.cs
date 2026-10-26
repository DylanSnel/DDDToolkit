using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DDDToolkit.Supporting.Tenancy.Analyzers;

/// <summary>
/// What an application's row access rules may ask of Tenancy's tables.
/// <list type="bullet">
/// <item>DDD00100: a restrictive rule, <c>[RowAccess&lt;T&gt;(..., Restrictive = true)]</c>, narrows the tables of
/// Tenancy's organization, its seats and its roles, and those of the application's own aggregates. On a tenant or an
/// invitation it would narrow what Tenancy's own work and the person accepting an invitation read, so it is
/// refused there.</item>
/// <item>DDD00101: <c>TenancyRowAccess.CallerSeat&lt;TSeat&gt;()</c> answers with the application's seat class, the one
/// declared with <c>[SeatAggregate]</c>; another type is no row the function answers with.</item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenancyRowAccessAnalyzer : DiagnosticAnalyzer
{
    private const string RowAccess = "DDDToolkit.Abstractions.Attributes.RowAccessAttribute`1";
    private const string TenancyRowAccess = "DDDToolkit.Supporting.Tenancy.Access.TenancyRowAccess";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.RestrictiveRuleOutOfReach, DiagnosticDescriptors.CallerSeatOfAnotherType];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(TenancyRowAccess) is not { } rowAccess)
            {
                return;
            }

            start.RegisterSymbolAction(AnalyzeRule, SymbolKind.NamedType);
            start.RegisterOperationAction(operation => AnalyzeCallerSeat(operation, rowAccess), OperationKind.Invocation);
        });
    }

    private static void AnalyzeRule(SymbolAnalysisContext context)
    {
        var rule = (INamedTypeSymbol)context.Symbol;
        foreach (var attribute in rule.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true } type
                || type.OriginalDefinition.ContainingNamespace.ToDisplayString() + "." + type.OriginalDefinition.MetadataName != RowAccess
                || !attribute.NamedArguments.Any(static argument => argument.Key == "Restrictive" && argument.Value.Value is true)
                || type.TypeArguments[0] is not INamedTypeSymbol aggregate)
            {
                continue;
            }

            var parent = TenancyParentOf(aggregate);
            if (parent is "TenantAggregate" or "InvitationAggregate")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RestrictiveRuleOutOfReach,
                    attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? rule.Locations.FirstOrDefault(),
                    rule.Name,
                    aggregate.Name,
                    parent == "TenantAggregate" ? "a tenant" : "an invitation"));
            }
        }
    }

    private static void AnalyzeCallerSeat(OperationAnalysisContext context, INamedTypeSymbol rowAccess)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "CallerSeat"
            || !SymbolEqualityComparer.Default.Equals(method.ContainingType, rowAccess)
            || method.TypeArguments.Length != 1
            || method.TypeArguments[0] is not INamedTypeSymbol seat
            || seat.TypeKind == TypeKind.Error
            || TenancyParentOf(seat) == "SeatAggregate")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.CallerSeatOfAnotherType, invocation.Syntax.GetLocation(), seat.Name));
    }

    /// <summary>The name of the parent of Tenancy's that <paramref name="type"/> derives from, or null for none.</summary>
    private static string? TenancyParentOf(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ContainingNamespace?.ToDisplayString() == "DDDToolkit.Supporting.Tenancy"
                && current.Name is "TenantAggregate" or "OrganizationAggregate" or "SeatAggregate" or "RoleAggregate" or "InvitationAggregate")
            {
                return current.Name;
            }
        }

        return null;
    }
}
