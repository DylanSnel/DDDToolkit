using System.Text;
using DTS.DDD.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DTS.DDD.Analyzers;

/// <summary>
/// Generates the base type, constructors and equality members for <c>[SingleValueObject&lt;TValue&gt;]</c>
/// records, plus their always-valid twin. Both implement <c>ISingleValue&lt;TSelf, TValue&gt;</c> when the project
/// can see it, so a project that does not declare them can still store them as their value.
/// <para>
/// With the MSBuild property <c>DDD_ImplicitConversions</c> set to <c>true</c>, it also writes two implicit
/// conversions: from the record to its value, and from the value to the twin. The second validates, and throws
/// <c>InvalidValueObjectException</c> for an invalid value. A record whose author declared a conversion operator
/// gets no conversion from the generator, and the plain record never gets one from the value: a plain value object
/// may be invalid, and a silent conversion to it would hide that.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SingleValueObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var implicitConversions = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            provider.GlobalOptions.TryGetValue("build_property.DDD_ImplicitConversions", out var value)
            && string.Equals(value?.Trim(), "true", global::System.StringComparison.OrdinalIgnoreCase));

        context.RegisterSourceOutput(
            context.SingleValueObjects().Combine(implicitConversions),
            static (productionContext, pair) => Execute(productionContext, pair.Left, pair.Right));
    }

    private static void Execute(SourceProductionContext context, SingleValueObjectDefinition definition, bool implicitConversions)
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

                if (implicitConversions && definition.ValueConvertible && !definition.DeclaresConversion)
                {
                    writer.Line();
                    writer.Line("/// <summary>The value. Written because DDD_ImplicitConversions is true.</summary>");
                    writer.Line("public static implicit operator " + value.FullyQualifiedName + "(" + name + " value) => (value ?? throw new global::System.ArgumentNullException(nameof(value))).Value;");
                }

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

                if (implicitConversions && definition.ValueConvertible)
                {
                    writer.Line();
                    writer.Line("/// <summary>The value, validated. Throws InvalidValueObjectException when it is invalid. Written because DDD_ImplicitConversions is true.</summary>");
                    writer.Line("public static implicit operator " + validName + "(" + value.FullyQualifiedName + " value) => new(value);");
                }
            }
        }

        context.AddSource(type.HintName(), SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
