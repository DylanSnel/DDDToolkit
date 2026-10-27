using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Supporting.Tenancy.Analyzers;

/// <summary>
/// What a request marked <c>[Operator]</c> carries beside it.
/// <list type="bullet">
/// <item>DDD00103: <c>[Operator(Tenant = OperatorTenant.FromRequest)]</c> beside a member marked <c>[ForTenant]</c> names two
/// tenants for the work of one command.</item>
/// <item>DDD00104: <c>[InTenant]</c> or <c>[RequiresPermission]</c> beside <c>[Operator]</c> refuses every operator, so the
/// request refuses every caller.</item>
/// <item>DDD00105: <c>Tenant</c> or <c>Scope</c> on the <c>[Operator]</c> of a query of the Mediator library says nothing,
/// since nothing begins for a query.</item>
/// </list>
/// </summary>
/// <remarks>
/// It reads the attributes the request declares itself. It knows the package's attributes by their names, as text, as the
/// rest of this project does.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperatorRequestAnalyzer : DiagnosticAnalyzer
{
    private const string OperatorAttribute = "DDDToolkit.Supporting.Tenancy.Access.OperatorAttribute";
    private const string ForTenantAttribute = "DDDToolkit.Supporting.Tenancy.Access.ForTenantAttribute";
    private const string InTenantAttribute = "DDDToolkit.Supporting.Tenancy.Access.InTenantAttribute";
    private const string RequiresPermissionAttribute = "DDDToolkit.Access.RequiresPermissionAttribute";
    private const string MediatorBaseQuery = "Mediator.IBaseQuery";
    private const string MediatorBaseStreamQuery = "Mediator.IBaseStreamQuery";

    /// <summary>The value of <c>OperatorTenant.FromRequest</c>.</summary>
    private const int FromRequest = 1;

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.OperatorTenantTwice, DiagnosticDescriptors.OperatorBesideSeatRequirement, DiagnosticDescriptors.OperatorWorkOnAQuery];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(OperatorAttribute) is not { } operatorAttribute)
            {
                return;
            }

            var known = new Known(
                operatorAttribute,
                start.Compilation.GetTypeByMetadataName(ForTenantAttribute),
                start.Compilation.GetTypeByMetadataName(InTenantAttribute),
                start.Compilation.GetTypeByMetadataName(RequiresPermissionAttribute),
                start.Compilation.GetTypeByMetadataName(MediatorBaseQuery),
                start.Compilation.GetTypeByMetadataName(MediatorBaseStreamQuery));
            start.RegisterSymbolAction(symbol => Analyze(symbol, known), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, Known known)
    {
        var request = (INamedTypeSymbol)context.Symbol;
        var attributes = request.GetAttributes();
        var operatorAttribute = attributes.FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.Operator));
        if (operatorAttribute is null)
        {
            return;
        }

        var location = operatorAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? request.Locations.FirstOrDefault();

        // DDD00104: what no operator meets, stacked beside what only an operator meets.
        var against = attributes
            .Where(attribute => attribute.AttributeClass is { } attributeClass
                && (SymbolEqualityComparer.Default.Equals(attributeClass, known.InTenant) || SymbolEqualityComparer.Default.Equals(attributeClass, known.RequiresPermission)))
            .Select(static attribute => "[" + Short(attribute.AttributeClass!.Name) + "]")
            .Distinct()
            .ToList();
        if (against.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.OperatorBesideSeatRequirement, location, request.Name, DefinitionFactory.Listed(against)));
        }

        // DDD00103: the tenant the request names and the one the command names, for one piece of work.
        var tenant = operatorAttribute.NamedArguments.FirstOrDefault(static named => named.Key == "Tenant").Value;
        if (tenant.Value is int value && value == FromRequest && ForTenantMember(request, known.ForTenant, context) is { } member)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.OperatorTenantTwice, member.Location ?? location, request.Name, member.Name));
        }

        // DDD00105: where a query's work would act, which no work does.
        if (operatorAttribute.NamedArguments.Length > 0 && IsQuery(request, known))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.OperatorWorkOnAQuery, location, request.Name, Written(operatorAttribute, context)));
        }
    }

    /// <summary>The member the request marks <c>[ForTenant]</c>, with where the marker is written; null for none.</summary>
    private static (string Name, Location? Location)? ForTenantMember(INamedTypeSymbol request, INamedTypeSymbol? forTenant, SymbolAnalysisContext context)
    {
        if (forTenant is null)
        {
            return null;
        }

        for (var current = request; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (Marked(property.GetAttributes()) is { } marker)
                {
                    return (property.Name, marker);
                }
            }

            foreach (var constructor in current.InstanceConstructors)
            {
                foreach (var parameter in constructor.Parameters)
                {
                    if (Marked(parameter.GetAttributes()) is { } marker)
                    {
                        return (parameter.Name, marker);
                    }
                }
            }
        }

        return null;

        Location? Marked(ImmutableArray<AttributeData> attributes)
            => attributes.FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, forTenant)) is { } marker
                ? marker.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? Location.None
                : null;
    }

    /// <summary>Whether the request is a query of the Mediator library, answered once or with a stream.</summary>
    private static bool IsQuery(INamedTypeSymbol request, Known known)
        => request.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, known.Query) || SymbolEqualityComparer.Default.Equals(implemented, known.StreamQuery));

    /// <summary>The attribute's arguments as the request writes them: <c>Tenant = OperatorTenant.FromRequest</c>.</summary>
    private static string Written(AttributeData attribute, SymbolAnalysisContext context)
        => attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken) is AttributeSyntax { ArgumentList: { } arguments }
            ? string.Join(", ", arguments.Arguments.Select(static argument => argument.ToString()))
            : string.Join(", ", attribute.NamedArguments.Select(static named => named.Key + " = " + named.Value.Value));

    /// <summary>An attribute's name without its <c>Attribute</c> ending.</summary>
    private static string Short(string name)
        => name.EndsWith("Attribute", System.StringComparison.Ordinal) && name.Length > "Attribute".Length ? name.Substring(0, name.Length - "Attribute".Length) : name;

    /// <summary>The types the analyzer compares with, found once per compilation.</summary>
    private sealed record Known(
        INamedTypeSymbol Operator,
        INamedTypeSymbol? ForTenant,
        INamedTypeSymbol? InTenant,
        INamedTypeSymbol? RequiresPermission,
        INamedTypeSymbol? Query,
        INamedTypeSymbol? StreamQuery);
}
