using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Generates the base type, constructors and equality members for <c>[SingleValueObject&lt;TValue&gt;]</c>
/// records, plus their always-valid twin. Both implement <c>ISingleValue&lt;TSelf, TValue&gt;</c> when the project
/// can see it, so a project that does not declare them can still store them as their value.
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
            var singleValue = definition.SingleValueAvailable
                ? ", " + Emit.SingleValueInterface(name, value.FullyQualifiedName)
                : string.Empty;
            using (writer.Block(type.PartialHeader + " : " + KnownTypes.BaseTypesNamespace + ".SingleValueObject<" + value.FullyQualifiedName + ">, "
                + KnownTypes.ValidationNamespace + ".IValidatable<" + validName + ">" + singleValue))
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

                if (definition.SingleValueAvailable)
                {
                    writer.Line();
                    Emit.SingleValueFromValue(writer, name, value.FullyQualifiedName);
                }
            }

            writer.Line();

            var twinSingleValue = definition.SingleValueAvailable
                ? ", " + Emit.SingleValueInterface(validName, value.FullyQualifiedName)
                : string.Empty;
            using (writer.Block(type.Accessibility + " partial record " + validName + " : " + name + ", " + KnownTypes.InterfacesNamespace + ".IAlwaysValid" + twinSingleValue))
            {
                // The record's copy constructor, not a property-by-property copy. It copies every field,
                // protected, private and get-only ones included; a copy of the settable properties alone
                // left the rest at their defaults, so the twin held a different value from the one validated.
                using (writer.Block(type.Accessibility + " " + validName + "(" + name + " value) : base(value)"))
                {
                    writer.Line("value.EnsureValidated();");
                    writer.Line("_isValid = true;");
                }

                writer.Line();
                using (writer.Block("public " + validName + "(" + value.FullyQualifiedName + " value) : base(value)"))
                {
                    writer.Line("EnsureValidated();");
                }

                writer.Line();
                Emit.SingleValueEqualityMembers(writer, validName, value.FullyQualifiedName, value.IsValueType);

                if (definition.SingleValueAvailable)
                {
                    writer.Line();
                    Emit.SingleValueFromValue(writer, validName, value.FullyQualifiedName);
                }
            }
        }

        context.AddSource(type.HintName(), SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
