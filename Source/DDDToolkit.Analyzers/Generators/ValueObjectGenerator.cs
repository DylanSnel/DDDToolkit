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
        var settableProperties = visibleProperties.Where(p => p.HasSetter).ToList();

        var withProperties = definition.GenerateWith ? settableProperties : new System.Collections.Generic.List<PropertyInfo>();
        var withParameters = string.Join(", ", withProperties.Select(p =>
            KnownTypes.BaseTypesNamespace + ".Optional<" + p.TypeName + "> " + Identifiers.ParameterNameFor(p.Name) + " = default"));
        var withArguments = string.Join(", ", withProperties.Select(p => Identifiers.ParameterNameFor(p.Name)));

        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader + " : " + KnownTypes.BaseTypesNamespace + ".ValueObject, "
                + KnownTypes.ValidationNamespace + ".IValidatable<" + validName + ">"))
            {
                EmitPositionalProperties(writer, definition.Properties.Where(p => p.IsPositional), definition.SystemTextJsonAvailable);

                EmitEqualityComponents(writer, comparisonProperties.Select(p => p.Name));
                writer.Line();
                Emit.RecordEqualityMembers(writer, name, hashCodeFromComponents: true);
                writer.Line();

                // A positional record already has its primary constructor, and every other constructor has
                // to chain to it. With no parameters that constructor is the parameterless one itself.
                var primaryParameters = definition.PrimaryConstructorParameterTypes;
                if (primaryParameters is not { Count: 0 })
                {
                    if (definition.SystemTextJsonAvailable)
                    {
                        writer.Line("[global::System.Text.Json.Serialization.JsonConstructor]");
                    }

                    var chain = primaryParameters is { } parameters
                        ? " : this(" + string.Join(", ", parameters.Select(parameterType => "default(" + parameterType + ")!")) + ")"
                        : string.Empty;

                    using (writer.Block("protected " + name + "()" + chain))
                    {
                    }

                    writer.Line();
                }

                writer.Line("/// <summary>The always-valid twin. Throws when the value is invalid; call TryToValid() to be handed the failures instead.</summary>");
                writer.Line(KnownTypes.InternalAttributeUsage);
                writer.Line("public " + validName + " ToValid() => new(this);");

                if (withProperties.Count > 0)
                {
                    writer.Line();
                    writer.Line("/// <summary>");
                    writer.Line("/// A copy with the given properties replaced; leave one out to keep it. The copy is judged");
                    writer.Line("/// afresh, like any new value. On the always-valid twin an invalid copy throws right here.");
                    writer.Line("/// </summary>");
                    EmitWithAttributes(writer, definition);
                    writer.Line("public virtual " + name + " With(" + withParameters + ")");
                    writer.Line("    => this with { " + string.Join(", ", withProperties.Select(p => p.Name + " = " + Identifiers.ParameterNameFor(p.Name) + ".Or(" + p.Name + ")")) + " };");
                }
            }

            writer.Line();

            using (writer.Block(type.Accessibility + " partial record " + validName + " : " + name + ", " + KnownTypes.InterfacesNamespace + ".IAlwaysValid"))
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
                EmitEqualityComponents(writer, comparisonProperties.Select(p => p.Name));
                writer.Line();
                Emit.RecordEqualityMembers(writer, validName, hashCodeFromComponents: false);

                if (withProperties.Count > 0)
                {
                    // Virtual, so a twin handed around as its base type still ends up here. The base makes
                    // the copy (a clone of this twin, its verdict cleared) and the constructor above judges
                    // it before anyone can hold on to it.
                    writer.Line();
                    writer.Line("/// <summary>A copy with the given properties replaced. Throws when the copy is not valid.</summary>");
                    EmitWithAttributes(writer, definition);
                    writer.Line("public override " + validName + " With(" + withParameters + ")");
                    writer.Line("    => new(base.With(" + withArguments + "));");
                }
            }
        }

        context.AddSource(type.HintName(), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    private static void EmitWithAttributes(CodeWriter writer, ValueObjectDefinition definition)
    {
        writer.Line(KnownTypes.InternalAttributeUsage);
        if (definition.GraphQLIgnoreAvailable)
        {
            writer.Line("[global::" + KnownTypes.GraphQLIgnoreAttribute + "]");
        }
    }

    /// <summary>
    /// Declares the properties of a positional record again, as <c>protected init</c>. A property declared
    /// under a parameter's name takes the place of the one the compiler would synthesize, and its
    /// initializer reads the parameter, so the primary constructor still fills it.
    /// </summary>
    private static void EmitPositionalProperties(CodeWriter writer, System.Collections.Generic.IEnumerable<PropertyInfo> properties, bool systemTextJsonAvailable)
    {
        foreach (var property in properties)
        {
            foreach (var attribute in property.Attributes)
            {
                writer.Line(attribute);
            }

            // The setter is protected, so System.Text.Json would read the property and never write it:
            // the [JsonConstructor] below builds the record from defaults and the values are dropped. A
            // value object inside a domain event would come out of the outbox empty. [JsonInclude] lets
            // the serializer reach the protected init, the way it has to for a declared property too.
            if (systemTextJsonAvailable && !property.Attributes.Any(attribute => attribute.Contains("JsonInclude")))
            {
                writer.Line("[global::System.Text.Json.Serialization.JsonInclude]");
            }

            writer.Line("public " + property.TypeName + " " + property.Name + " { get; protected init; } = " + property.Name + ";");
            writer.Line();
        }
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
