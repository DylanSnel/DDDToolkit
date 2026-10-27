using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes the class a package asks with <c>[assembly: TemplateFacade]</c> into the project that declares the classes
/// it is closed over. A package ships
/// <code>
/// [assembly: TemplateFacade(typeof(SeatManager&lt;,,,,,,,,&gt;))]
/// </code>
/// and the domain project of the module Tenants, which declares the classes with the package's templates, gets
/// <code>
/// public sealed class SeatManager : global::Acme.Tenancy.SeatManager&lt;global::Shop.Domain.Tenant, ...&gt;
/// {
///     public SeatManager(...) : base(...) { }
/// }
/// </code>
/// in the global namespace, so that project and every project above it take a <c>SeatManager</c> and name
/// <c>SeatManager.SeatOverview</c>, the package's own nested type, and none of them writes the nine types.
/// <c>[assembly: TemplateFacadeName("SeatManager", "CustomersSeats")]</c> in that project names it otherwise.
/// When the class cannot be written, it says why there, DDD00065, as information: the projects above would otherwise
/// only hear that the name does not exist. Two classes of one name where a project sees both are DDD00075, and a line
/// that names nothing is DDD00076. See <c>TemplateFacades</c>.
/// <para>
/// A package's class that is not abstract gets a sealed class the container makes, with its public constructors, which
/// the package's registration registers. A package's record that is not sealed gets a sealed record the application's
/// code makes, with its public constructors, as Tenancy's <c>TenantToProvision</c> does. An abstract class gets an
/// abstract class that is only a name. A project that says which of its ids are a package's with
/// <c>[assembly: TenancyIds&lt;...&gt;]</c> gets a class closed over them, in its root namespace.
/// </para>
/// <para>
/// A package's interface, <c>[assembly: TemplateFacade(typeof(ITenancyStore&lt;,,,,,,,,&gt;))]</c>, gets a global alias,
/// <c>global using TenancyStore = global::...ITenancyStore&lt;...&gt;;</c>, in the project that declares the classes and in
/// every project above it that sees them, since an alias holds in its own project only.
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
        if (file.IsAlias)
        {
            // A package's interface: an alias, which holds in this project only, so each project that sees the classes
            // has its own. A comment says what it is closed over, since an alias carries no documentation.
            writer.Line("// " + file.Summary);
            writer.Line("// " + file.Naming);
            writer.Line("global using " + file.Name + " = " + file.BaseType + ";");
            context.AddSource(file.HintName, SourceText.From(writer.ToString(), Encoding.UTF8));
            return;
        }

        if (file.Namespace.Length > 0)
        {
            writer.Line("namespace " + file.Namespace + ";");
            writer.Line();
        }

        writer.Line("/// <summary>");
        writer.Line("/// " + file.Summary);
        if (file.IsRecord)
        {
            // A record the application's code makes and hands to the package's methods.
            writer.Line("/// Your code makes it and hands it to the package's methods, which take the package's record it derives from:");
            writer.Line("/// every member is the package's own.");
            writer.Line("/// " + file.Naming);
            writer.Line("/// </summary>");
            WriteSealed(writer, file, "record", "Made with what the package's record takes.");
        }
        else if (file.IsService)
        {
            // A class the container makes: the package's registration registers this one, and code takes it.
            writer.Line("/// The container makes it, and the package's registration registers it: your code takes it, and every member is the");
            writer.Line("/// package's own.");
            writer.Line("/// " + file.Naming);
            writer.Line("/// </summary>");
            WriteSealed(writer, file, "class", "Made by the container, with what the package's class asks for.");
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

    /// <summary>A sealed class or record with each public constructor of the package's type, passed on to it.</summary>
    private static void WriteSealed(CodeWriter writer, FacadeFile file, string kind, string constructorSummary)
    {
        using (writer.Block((file.IsPublic ? "public" : "internal") + " sealed " + kind + " " + file.Name + " : " + file.BaseType))
        {
            var first = true;
            foreach (var constructor in file.Constructors)
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;
                writer.Line("/// <summary>" + constructorSummary + "</summary>");
                writer.Line("public " + file.Name + "(" + constructor.Parameters + ")");
                writer.Line("    : base(" + constructor.Arguments + ")");
                writer.Line("{");
                writer.Line("}");
            }
        }
    }
}
