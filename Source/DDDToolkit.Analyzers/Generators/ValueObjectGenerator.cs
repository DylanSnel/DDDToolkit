using System.Linq;
using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Generates the base type, structural equality and the always-valid twin for <c>[ValueObject]</c> records.
/// Equality covers every property except those marked <c>[Internal]</c> or <c>[DontCompare]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ValueObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.ValueObjects(), static (productionContext, definition) => Execute(productionContext, definition));
    }

    private static void Execute(SourceProductionContext context, ValueObjectDefinition definition)
    {
        definition.Diagnostics.ReportAll(context);
        if (!definition.CanGenerate)
        {
            return;
        }

        var type = definition.Type;
        var name = type.Name;
        var validName = type.ValidTwinName;

        var visibleProperties = definition.Properties.Where(p => !p.IsInternal).ToList();
        var comparisonProperties = visibleProperties.Where(p => !p.IsDontCompare).ToList();
        var copiedProperties = visibleProperties.Where(p => p.HasSetter).ToList();

        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader + " : " + KnownTypes.BaseTypesNamespace + ".ValueObject, "
                + KnownTypes.ValidationNamespace + ".IValidatable<" + validName + ">"))
            {
                EmitEqualityComponents(writer, comparisonProperties.Select(p => p.Name));
                writer.Line();
                Emit.RecordEqualityMembers(writer, name, hashCodeFromComponents: true);
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
                writer.Line(KnownTypes.InternalAttributeUsage);
                writer.Line("public " + validName + " ToValid() => new(this);");
            }

            writer.Line();

            using (writer.Block(type.Accessibility + " partial record " + validName + " : " + name + ", " + KnownTypes.InterfacesNamespace + ".IAlwaysValid"))
            {
                using (writer.Block(type.Accessibility + " " + validName + "(" + name + " value)"))
                {
                    writer.Line("value.EnsureValidated();");
                    foreach (var property in copiedProperties)
                    {
                        writer.Line("this." + property.Name + " = value." + property.Name + ";");
                    }

                    writer.Line("_isValid = true;");
                }

                writer.Line();
                EmitEqualityComponents(writer, comparisonProperties.Select(p => p.Name));
                writer.Line();
                Emit.RecordEqualityMembers(writer, validName, hashCodeFromComponents: false);
            }
        }

        context.AddSource(type.HintName(), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    private static void EmitEqualityComponents(CodeWriter writer, System.Collections.Generic.IEnumerable<string> propertyNames)
    {
        writer.Line(KnownTypes.InternalAttributeUsage);
        using (writer.Block("protected override global::System.Collections.Generic.IEnumerable<object?> GetEqualityComponents()"))
        {
            var any = false;
            foreach (var propertyName in propertyNames)
            {
                any = true;
                writer.Line("yield return " + propertyName + ";");
            }

            if (!any)
            {
                writer.Line("yield break;");
            }
        }
    }
}
