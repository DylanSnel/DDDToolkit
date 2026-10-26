using System;
using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Analyzers.Analyzers;

/// <summary>
/// DDD00092: the shape of a module's permissions, which the packages' generators read and nothing else. A class marked
/// <c>[Permissions]</c> is static; every <c>public const string</c> of it is a permission and carries a level, an
/// attribute derived from <c>PermissionLevelAttribute</c>; a level is on such a constant and nowhere else; and a permission
/// is declared once in a class.
/// <para>
/// DDD00099: a permission says what it lets somebody do, for the screens that put permissions into roles, with
/// <c>Description</c> on its level or in the constant's <c>&lt;summary&gt;</c>; a retired one is shown nowhere and needs
/// neither.
/// </para>
/// <para>
/// The levels are the packages' own, Tenancy's <c>[AtTenant]</c> and <c>[AtUnit]</c> and Membership's
/// <c>[On&lt;TResource&gt;]</c>, and the core knows them only by what they derive from. Whether a level fits what it is
/// asked on is the package's to say. <c>[AtUnit]</c> on a member of a request is the request's marker, and is not
/// judged here.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PermissionsAnalyzer : DiagnosticAnalyzer
{
    private const string Marker = "DDDToolkit.Access.PermissionsAttribute";
    private const string Level = "DDDToolkit.Access.PermissionLevelAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.PermissionMisdeclared, DiagnosticDescriptors.PermissionWithoutDescription];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(Marker) is not { } marker || start.Compilation.GetTypeByMetadataName(Level) is not { } level)
            {
                return;
            }

            start.RegisterSymbolAction(symbol => AnalyzeClass(symbol, marker, level), SymbolKind.NamedType);
            start.RegisterSymbolAction(symbol => AnalyzeField(symbol, marker, level), SymbolKind.Field);
        });
    }

    private static void AnalyzeClass(SymbolAnalysisContext context, INamedTypeSymbol marker, INamedTypeSymbol level)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!type.GetAttributes().Any(each => SymbolEqualityComparer.Default.Equals(each.AttributeClass, marker)))
        {
            return;
        }

        if (!type.IsStatic)
        {
            Report(context, type.Locations.FirstOrDefault(), type.Name,
                "is marked [Permissions] and is not a static class. A class of permissions holds constants and nothing else: declare it public static class " + type.Name + ".");
        }

        // A permission declared twice: the second is the slip.
        foreach (var twice in type.GetMembers().OfType<IFieldSymbol>()
                     .Where(static field => field is { IsConst: true, DeclaredAccessibility: Accessibility.Public, ConstantValue: string })
                     .GroupBy(static field => (string)field.ConstantValue!, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            foreach (var field in twice.Skip(1))
            {
                Report(context, field.Locations.FirstOrDefault(), type.Name + "." + field.Name,
                    "declares '" + twice.Key + "', which '" + twice.First().Name + "' declares already. A permission is declared once; name it by that constant.");
            }
        }
    }

    private static void AnalyzeField(SymbolAnalysisContext context, INamedTypeSymbol marker, INamedTypeSymbol level)
    {
        var field = (IFieldSymbol)context.Symbol;
        var levels = field.GetAttributes().Where(each => DerivesFrom(each.AttributeClass, level)).ToList();
        var inPermissions = field.ContainingType.GetAttributes().Any(each => SymbolEqualityComparer.Default.Equals(each.AttributeClass, marker));
        var isPermission = field is { IsConst: true, DeclaredAccessibility: Accessibility.Public } && field.Type.SpecialType == SpecialType.System_String;
        var at = field.ContainingType.Name + "." + field.Name;

        if (levels.Count > 0 && !inPermissions)
        {
            Report(context, Where(levels[0], field), at,
                "carries a level, and '" + field.ContainingType.Name + "' is not marked [Permissions], so nothing reads it. Mark the class [Permissions], or take the level off.");
        }
        else if (levels.Count > 0 && !isPermission)
        {
            Report(context, Where(levels[0], field), at,
                "carries a level, and is no public const string: a permission is a constant a request names in [RequiresPermission], public so the modules that ask it can. Declare it public const string.");
        }
        else if (levels.Count == 0 && inPermissions && isPermission)
        {
            Report(context, field.Locations.FirstOrDefault(), at,
                "is a permission without a level: say where it is asked, [AtTenant] for the whole tenant or [AtUnit] at a unit (Tenancy), or [On<TResource>] on a resource with members (Membership).");
        }
        else if (levels.Count > 0 && inPermissions && isPermission
                 && !PermissionDescriptions.Retired(levels)
                 && PermissionDescriptions.Said(levels) is null
                 && PermissionDescriptions.Summary(field, context.CancellationToken) is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PermissionWithoutDescription, field.Locations.FirstOrDefault(), at, AsWritten(levels[0])));
        }
    }

    /// <summary>A level as code writes it: <c>AtUnit</c>, <c>On&lt;Board&gt;</c>.</summary>
    private static string AsWritten(AttributeData level)
    {
        var type = level.AttributeClass!;
        var name = type.Name.EndsWith("Attribute", StringComparison.Ordinal) ? type.Name.Substring(0, type.Name.Length - "Attribute".Length) : type.Name;
        return type.TypeArguments.Length == 0 ? name : name + "<" + string.Join(", ", type.TypeArguments.Select(static argument => argument.Name)) + ">";
    }

    private static Location? Where(AttributeData attribute, ISymbol symbol)
        => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? symbol.Locations.FirstOrDefault();

    private static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol level)
    {
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, level))
            {
                return true;
            }
        }

        return false;
    }

    private static void Report(SymbolAnalysisContext context, Location? location, string at, string what)
        => context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PermissionMisdeclared, location, at, what));
}
