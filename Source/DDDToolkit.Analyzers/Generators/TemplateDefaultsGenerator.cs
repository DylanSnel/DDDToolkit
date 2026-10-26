using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes what a package's switch asks for and its author would otherwise have written: the declarations of the
/// classes and ids a project leaves out. A package marks an assembly attribute with
/// <code>
/// [TemplateDefaults(typeof(TenantAggregateAttribute&lt;&gt;), typeof(OrganizationAggregateAttribute&lt;&gt;), ...)]
/// public sealed class GenerateTenancyClassesAttribute : Attribute;
/// </code>
/// and a project that says <c>[assembly: GenerateTenancyClasses]</c>, and declares no tenant, gets
/// <code>
/// [global::Acme.Tenancy.TenantAggregateAttribute&lt;global::Shop.TenantId&gt;]
/// public sealed partial class Tenant
/// {
/// }
///
/// [global::DDDToolkit.Abstractions.Attributes.ModuleContract]
/// [global::DDDToolkit.Abstractions.Attributes.EntityId&lt;global::System.Guid&gt;]
/// public readonly partial record struct TenantId;
/// </code>
/// each documented with the switch that wrote it and how to declare it yourself instead.
/// <para>
/// That is all this generator writes. Everything else a class or an id gets, the base class and the converters,
/// the registrations closed over the classes and the class the use cases are named through, the generators that
/// write it for a declared class write for these too, because the providers they read hand these on beside the
/// declared ones (<see cref="TemplateDefaults"/>). The attributes are written for the projects above, which see the
/// compiled classes the way they see declared ones, and for the analyzers, which read the compilation after it.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TemplateDefaultsGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var plan = context.TemplateDefaultsPlan();
        context.RegisterSourceOutput(plan.SelectMany(static (each, _) => each.Diagnostics), static (productionContext, diagnostic) => diagnostic.Report(productionContext));
        context.RegisterSourceOutput(plan.SelectMany(static (each, _) => each.Declarations), static (productionContext, declaration) => Execute(productionContext, declaration));
    }

    private static void Execute(SourceProductionContext context, DefaultDeclaration declaration)
    {
        var writer = new CodeWriter().Header();
        if (declaration.Namespace.Length > 0)
        {
            writer.Line("namespace " + declaration.Namespace + ";");
            writer.Line();
        }

        foreach (var line in declaration.Documentation)
        {
            writer.Line("/// " + line);
        }

        foreach (var attribute in declaration.Attributes)
        {
            writer.Line(attribute);
        }

        if (declaration.Declaration.EndsWith(";", System.StringComparison.Ordinal))
        {
            writer.Line(declaration.Declaration);
        }
        else
        {
            // Braces rather than a semicolon: an empty class body is a semicolon only from C# 12 on.
            using (writer.Block(declaration.Declaration))
            {
            }
        }

        context.AddSource(declaration.HintName, SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
