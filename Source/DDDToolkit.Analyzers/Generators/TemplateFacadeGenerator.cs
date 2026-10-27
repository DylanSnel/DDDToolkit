using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes the class a package asks with <c>[assembly: TemplateFacade]</c> into the project that declares the classes
/// it is closed over. A package ships
/// <code>
/// [assembly: TemplateFacade(typeof(TenancyUseCases&lt;,,,,,,,,&gt;))]
/// </code>
/// and the domain project of the module Tenants, which declares the classes with the package's templates, gets
/// <code>
/// public abstract class TenancyUseCases : global::Acme.Tenancy.TenancyUseCases&lt;global::Shop.Domain.Tenant, ...&gt;
/// {
///     private TenancyUseCases() { }
/// }
/// </code>
/// in the global namespace, so that project and every project above it name <c>TenancyUseCases.SeatCommands</c> and
/// <c>TenancyUseCases.SeatOverview</c>, the package's own nested types, and none of them writes the nine types.
/// <c>[assembly: TemplateFacadeName("TenancyUseCases", "CustomersTenancy")]</c> in that project names it otherwise.
/// When the class cannot be written, it says why there, DDD00065, as information: the projects above would otherwise
/// only hear that the name does not exist. Two classes of one name where a project sees both are DDD00075, and a line
/// that names nothing is DDD00076. See <c>TemplateFacades</c>.
/// <para>
/// A package's class that is not abstract, Tenancy's <c>TenancyAccessManager&lt;...&gt;</c>, gets a sealed class the
/// container makes, with its public constructors, which the package's registration registers. A project that says which
/// of its ids are a package's with <c>[assembly: TenancyIds&lt;...&gt;]</c> gets a class closed over them, in its root
/// namespace.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TemplateFacadeGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.RegisterSourceOutput(context.TemplateFacadeFiles(), static (productionContext, outcome) => Execute(productionContext, outcome));

    private static void Execute(SourceProductionContext context, FacadeOutcome outcome)
    {
        outcome.Diagnostic?.Report(context);
        if (outcome.File is not { } file)
        {
            return;
        }

        var writer = new CodeWriter().Header();
        if (file.Namespace.Length > 0)
        {
            writer.Line("namespace " + file.Namespace + ";");
            writer.Line();
        }

        writer.Line("/// <summary>");
        writer.Line("/// " + file.Summary);
        if (file.IsService)
        {
            // A class the container makes: the package's registration registers this one, and code takes it.
            writer.Line("/// The container makes it, and the package's registration registers it: your code takes it, and every member is the");
            writer.Line("/// package's own.");
            writer.Line("/// " + file.Naming);
            writer.Line("/// </summary>");
            using (writer.Block((file.IsPublic ? "public" : "internal") + " sealed class " + file.Name + " : " + file.BaseType))
            {
                var first = true;
                foreach (var constructor in file.Constructors)
                {
                    if (!first)
                    {
                        writer.Line();
                    }

                    first = false;
                    writer.Line("/// <summary>Made by the container, with what the package's class asks for.</summary>");
                    writer.Line("public " + file.Name + "(" + constructor.Parameters + ")");
                    writer.Line("    : base(" + constructor.Arguments + ")");
                    writer.Line("{");
                    writer.Line("}");
                }
            }
        }
        else
        {
            writer.Line("/// Every type nested in it, and every static member, is named through this class and is the package's own: the type");
            writer.Line("/// its registration added, the method it declares.");
            writer.Line("/// The class is only that name. Nothing makes one, and nothing derives from it.");
            writer.Line("/// " + file.Naming);
            writer.Line("/// </summary>");
            using (writer.Block((file.IsPublic ? "public" : "internal") + " abstract class " + file.Name + " : " + file.BaseType))
            {
                writer.Line("private " + file.Name + "()");
                writer.Line("{");
                writer.Line("}");
            }
        }

        context.AddSource(file.HintName, SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
