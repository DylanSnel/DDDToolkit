using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Analyzers.Analyzers;

/// <summary>
/// Reports a member a library marks for a package's row access contribution that the project that runs the Supabase
/// export cannot see: DDD00070, on a property or field marked with an attribute whose class carries
/// <c>[ApplicationMark]</c>, such as <c>[TenancyCatalogue]</c>, where it is not public, in public types, with a
/// public getter.
/// <para>
/// The contribution is made in the project that runs the export, from the marks it finds in the projects it
/// references. Of a library it sees the public surface and nothing else: a member that is internal is not there for
/// it, so it would make Tenancy's policies from the default catalogue, or leave a resource's functions out, and
/// could not say why. Only the library that declares the member knows it is marked, so it is reported there.
/// </para>
/// <para>
/// Silent in an application: the program that runs the export, or the host, reads its own marks, and nothing
/// references it for them. Whether the member is static, has a getter and is of the type the package takes, the
/// export says where it runs (DDD00072), since it sees such a member.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApplicationMarkAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.ApplicationMarkNotPublic];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.Options.OutputKind is not (OutputKind.DynamicallyLinkedLibrary or OutputKind.NetModule)
                || start.Compilation.GetTypeByMetadataName(KnownTypes.ApplicationMarkAttribute) is not { } applicationMark)
            {
                return;
            }

            start.RegisterSymbolAction(symbol => Analyze(symbol, applicationMark), SymbolKind.Property, SymbolKind.Field);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol applicationMark)
    {
        var member = context.Symbol;
        if (member.IsImplicitlyDeclared || IsPublic(member))
        {
            return;
        }

        var mark = member.GetAttributes()
            .Select(static attribute => attribute.AttributeClass)
            .FirstOrDefault(attribute => attribute is not null && attribute.OriginalDefinition.GetAttributes().Any(each => SymbolEqualityComparer.Default.Equals(each.AttributeClass, applicationMark)));
        if (mark is not null && member.Locations.FirstOrDefault(static location => location.IsInSource) is { } location)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ApplicationMarkNotPublic,
                location,
                member.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                Shown(mark)));
        }
    }

    /// <summary>Whether the member, every type it is declared in, and a property's getter are public: what a project that references this one sees.</summary>
    private static bool IsPublic(ISymbol member)
    {
        for (ISymbol? symbol = member; symbol is not null and not INamespaceSymbol; symbol = symbol.ContainingSymbol)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return member is not IPropertySymbol { GetMethod: { DeclaredAccessibility: not Accessibility.Public } };
    }

    /// <summary>The attribute as an application writes it: <c>[TenancyCatalogue]</c>, <c>[MembershipRules&lt;CrewMember&gt;]</c>.</summary>
    private static string Shown(INamedTypeSymbol mark)
    {
        const string Suffix = "Attribute";
        var name = mark.Name.EndsWith(Suffix, System.StringComparison.Ordinal) && mark.Name.Length > Suffix.Length ? mark.Name.Substring(0, mark.Name.Length - Suffix.Length) : mark.Name;
        return "[" + name + (mark.IsGenericType ? "<" + string.Join(", ", mark.TypeArguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ">" : string.Empty) + "]";
    }
}
