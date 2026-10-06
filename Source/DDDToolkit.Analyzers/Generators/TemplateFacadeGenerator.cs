using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes the class a package asks with <c>[assembly: TemplateFacade]</c> into the project that declares the classes
/// it is closed over. A package ships
/// <code>
/// [assembly: TemplateFacade(typeof(TenancyUseCases&lt;,,,,,,,,&gt;), "{Module}Tenancy")]
/// </code>
/// and the domain project of the module Tenants, which declares the classes with the package's templates, gets
/// <code>
/// public abstract class TenantsTenancy : global::Acme.Tenancy.TenancyUseCases&lt;global::Shop.Domain.Tenant, ...&gt;
/// {
///     private TenantsTenancy() { }
/// }
/// </code>
/// in the global namespace, so that project and every project above it name <c>TenantsTenancy.SeatCommands</c> and
/// <c>TenantsTenancy.SeatOverview</c>, the package's own nested types, and none of them writes the nine types.
/// When the class cannot be written, it says why there, DDD00065, as information: the projects above would otherwise
/// only hear that the name does not exist. See <c>TemplateFacades</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TemplateFacadeGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.RegisterSourceOutput(context.TemplateFacadeFiles(), static (productionContext, outcome) => Execute(productionContext, outcome));

    private static void Execute(SourceProductionContext context, FacadeOutcome outcome)
    {
        outcome.NotWritten?.Report(context);
        if (outcome.File is not { } file)
        {
            return;
        }

        var writer = new CodeWriter().Header();
        writer.Line("/// <summary>");
        writer.Line("/// " + file.Summary);
        writer.Line("/// Every type nested in it is named through this class, and is the package's own: the type its registration added.");
        writer.Line("/// The class is only that name. Nothing makes one, and nothing derives from it.");
        writer.Line("/// </summary>");
        using (writer.Block((file.IsPublic ? "public" : "internal") + " abstract class " + file.Name + " : " + file.BaseType))
        {
            writer.Line("private " + file.Name + "()");
            writer.Line("{");
            writer.Line("}");
        }

        context.AddSource(file.HintName, SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
