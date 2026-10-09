using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes a package's <c>[TemplateRegistration]</c> methods into the project that declares the classes they
/// are about, closed over those classes. A package ships
/// <code>
/// [TemplateRegistration]
/// public static ModelBuilder AddTenancy&lt;[TemplateType(typeof(TenantAggregateAttribute&lt;&gt;), Take = TemplateArgumentKind.Type)] TTenant, ...&gt;(this ModelBuilder modelBuilder)
/// </code>
/// and the project that declares <c>[TenantAggregate&lt;TenantId&gt;] partial class ShopTenant</c> and the rest gets
/// <code>
/// internal static partial class GeneratedTenancyModelBuilderExtensions
/// {
///     public static ModelBuilder AddTenancy(this ModelBuilder modelBuilder)
///         => TenancyModelBuilderExtensions.AddTenancy&lt;ShopTenant, TenantId, ...&gt;(modelBuilder);
/// }
/// </code>
/// in the package's namespace, so <c>modelBuilder.AddTenancy()</c> needs no using it did not already have.
/// <para>
/// The wrapper is internal: two projects of one application that both declare the classes each get their
/// own, and neither sees the other's. It is a partial class because each method name gets a file of its own.
/// </para>
/// <para>
/// A project that declares none of the classes gets the wrapper as well when it declares the same
/// <c>[assembly: Module]</c> as the projects that do: a module's infrastructure project, where the context is,
/// next to its domain project, which then needs no Entity Framework. See <c>TemplateRegistrations</c>.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TemplateRegistrationGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.RegisterSourceOutput(context.TemplateRegistrationFiles(), static (productionContext, file) => Execute(productionContext, file));

    private static void Execute(SourceProductionContext context, RegistrationFile file)
    {
        file.Diagnostics.ReportAll(context);
        if (file.Wrappers.Count == 0)
        {
            return;
        }

        var writer = new CodeWriter().Header();
        if (file.Namespace.Length > 0)
        {
            writer.Line("namespace " + file.Namespace + ";");
            writer.Line();
        }

        writer.Line("/// <summary>" + file.DeclaringType + "'s registrations, closed over this project's classes.</summary>");
        using (writer.Block("internal static partial class " + file.ClassName))
        {
            var first = true;
            foreach (var wrapper in file.Wrappers)
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;
                writer.Line("/// <summary>" + wrapper.Summary + "</summary>");
                writer.Line(wrapper.Signature);
                foreach (var constraint in wrapper.Constraints)
                {
                    writer.Line("    " + constraint);
                }

                writer.Line("    => " + wrapper.Call + ";");
            }
        }

        context.AddSource(file.HintName, SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
