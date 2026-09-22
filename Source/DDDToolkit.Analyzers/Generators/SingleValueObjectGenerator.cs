using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Generates the base type, constructors and equality members for <c>[SingleValueObject&lt;TValue&gt;]</c>
/// records, plus their always-valid twin.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SingleValueObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.SingleValueObjects(), static (productionContext, definition) => Execute(productionContext, definition));
    }

    private static void Execute(SourceProductionContext context, SingleValueObjectDefinition definition)
    {
        definition.Diagnostics.ReportAll(context);
        if (!definition.CanGenerate)
        {
            return;
        }

        var type = definition.Type;
        var value = definition.Value;
        var name = type.Name;
        var validName = type.ValidTwinName;
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader + " : " + KnownTypes.BaseTypesNamespace + ".SingleValueObject<" + value.FullyQualifiedName + ">, "
                + KnownTypes.ValidationNamespace + ".IValidatable<" + validName + ">"))
            {
                Emit.SingleValueEqualityMembers(writer, name, value.FullyQualifiedName, value.IsValueType);
                writer.Line();

                using (writer.Block("protected " + name + "(" + value.FullyQualifiedName + " value) : base(value)"))
                {
                }

                writer.Line();
                if (definition.SystemTextJsonAvailable)
                {
                    writer.Line("[global::System.Text.Json.Serialization.JsonConstructor]");
                }

                using (writer.Block("protected " + name + "()"))
                {
                }

                writer.Line();
                writer.Line("/// <summary>The always-valid twin. Throws when the value is invalid; call TryToValid() to be handed the failures instead.</summary>");
                writer.Line("public " + validName + " ToValid() => new(this);");
            }

            writer.Line();

            using (writer.Block(type.Accessibility + " partial record " + validName + " : " + name + ", " + KnownTypes.InterfacesNamespace + ".IAlwaysValid"))
            {
                using (writer.Block(type.Accessibility + " " + validName + "(" + name + " value)"))
                {
                    writer.Line("value.EnsureValidated();");
                    writer.Line("this.Value = value.Value;");
                    writer.Line("_isValid = true;");
                }

                writer.Line();
                using (writer.Block("public " + validName + "(" + value.FullyQualifiedName + " value) : base(value)"))
                {
                    writer.Line("EnsureValidated();");
                }

                writer.Line();
                Emit.SingleValueEqualityMembers(writer, validName, value.FullyQualifiedName, value.IsValueType);
            }
        }

        context.AddSource(type.HintName(), SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
