using System.Collections.Generic;
using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Generates strongly typed ids from <c>[EntityId&lt;TValue&gt;]</c>.
/// <list type="bullet">
/// <item><c>partial record</c>: derives from <c>EntityId&lt;TValue&gt;</c> and gets an always-valid twin, as before.</item>
/// <item><c>readonly partial record struct</c>: a self-contained, allocation-free id implementing
/// <c>IEntityId&lt;TValue&gt;</c>, with parsing, comparison, conversions and a System.Text.Json converter.</item>
/// </list>
/// <para>
/// It also generates the ids <c>[AggregateRoot&lt;Guid&gt;]</c> and <c>[Entity&lt;Guid&gt;]</c> ask for, which the
/// shared provider yields alongside the declared ones. Those take the struct form and go through the
/// same emitter, so an implicit id has exactly the surface an explicit one has.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityIdGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.EntityIds(), static (productionContext, definition) => Execute(productionContext, definition));
    }

    private static void Execute(SourceProductionContext context, EntityIdDefinition definition)
    {
        definition.Diagnostics.ReportAll(context);
        if (!definition.CanGenerate)
        {
            return;
        }

        var source = definition.Type.Kind == DeclarationKind.RecordStruct
            ? EmitStruct(definition)
            : EmitClass(definition);

        context.AddSource(definition.Type.HintName(), SourceText.From(source, Encoding.UTF8));
    }

    // ------------------------------------------------------------------ record class

    private static string EmitClass(EntityIdDefinition definition)
    {
        var type = definition.Type;
        var value = definition.Value;
        var name = type.Name;
        var validName = type.ValidTwinName;
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader + " : " + KnownTypes.BaseTypesNamespace + ".EntityId<" + value.FullyQualifiedName + ">, "
                + KnownTypes.ValidationNamespace + ".IValidatable<" + validName + ">"))
            {
                writer.Line(PrefixDocComment(value.CanParse));
                writer.Line("public const string IdPrefix = \"" + Escape(definition.Prefix) + "\";");
                writer.Line();

                using (writer.Block("protected " + name + "(" + value.FullyQualifiedName + " value) : base(value, IdPrefix)"))
                {
                }

                writer.Line();
                if (definition.SystemTextJsonAvailable)
                {
                    writer.Line("[global::System.Text.Json.Serialization.JsonConstructor]");
                }

                using (writer.Block("protected " + name + "() : base(IdPrefix)"))
                {
                }

                writer.Line();
                Emit.SingleValueEqualityMembers(writer, name, value.FullyQualifiedName, value.IsValueType);

                if (value.IsGuid)
                {
                    writer.Line();
                    EmitGuidFactories(writer, name);
                }

                if (value.CanParse)
                {
                    writer.Line();
                    EmitParse(writer, definition, isStruct: false);
                }

                writer.Line();
                writer.Line("/// <summary>The always-valid twin. Throws when the id is invalid; call TryToValid() to be handed the failures instead.</summary>");
                writer.Line("public " + validName + " ToValid() => new(this);");
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
                using (writer.Block("public " + validName + "(" + value.FullyQualifiedName + " value) : base(value)"))
                {
                    writer.Line("EnsureValidated();");
                }

                writer.Line();
                Emit.SingleValueEqualityMembers(writer, validName, value.FullyQualifiedName, value.IsValueType);
            }
        }

        return writer.ToString();
    }

    // ------------------------------------------------------------------ record struct

    private static string EmitStruct(EntityIdDefinition definition)
    {
        var type = definition.Type;
        var value = definition.Value;
        var name = type.Name;
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            if (definition.SystemTextJsonAvailable)
            {
                writer.Line("[global::System.Text.Json.Serialization.JsonConverter(typeof(" + name + ".SystemTextJsonConverter))]");
            }

            var interfaces = new List<string>
            {
                KnownTypes.InterfacesNamespace + ".IEntityId<" + value.FullyQualifiedName + ">",
                "global::System.IComparable<" + name + ">",
            };

            var implementParsable = value.CanParse && definition.IParsableAvailable;
            if (implementParsable)
            {
                interfaces.Add("global::System.IParsable<" + name + ">");
            }

            using (writer.Block(type.PartialHeader + " : " + string.Join(", ", interfaces)))
            {
                writer.Line(PrefixDocComment(value.CanParse));
                writer.Line("public const string IdPrefix = \"" + Escape(definition.Prefix) + "\";");
                writer.Line();
                writer.Line("public " + value.FullyQualifiedName + " Value { get; }");
                writer.Line();

                using (writer.Block("public " + name + "(" + value.FullyQualifiedName + " value)"))
                {
                    writer.Line("Value = value;");
                }

                writer.Line();
                writer.Line("/// <summary>The default id (no value). Never identifies anything.</summary>");
                writer.Line("public static " + name + " Empty => default;");
                writer.Line();
                writer.Line("/// <summary>True when this id holds the default value.</summary>");
                writer.Line("public bool IsEmpty => global::System.Collections.Generic.EqualityComparer<" + value.FullyQualifiedName + ">.Default.Equals(Value, default" + (value.IsValueType ? string.Empty : "!") + ");");

                if (value.IsGuid)
                {
                    writer.Line();
                    EmitGuidFactories(writer, name);
                }

                writer.Line();
                EmitToString(writer, value);
                writer.Line();
                writer.Line("public int CompareTo(" + name + " other) => global::System.Collections.Generic.Comparer<" + value.FullyQualifiedName + ">.Default.Compare(Value, other.Value);");
                writer.Line();
                writer.Line("public static explicit operator " + value.FullyQualifiedName + "(" + name + " id) => id.Value;");
                writer.Line();
                writer.Line("public static explicit operator " + name + "(" + value.FullyQualifiedName + " value) => new(value);");

                if (value.CanParse)
                {
                    writer.Line();
                    EmitParse(writer, definition, isStruct: true);

                    if (implementParsable)
                    {
                        writer.Line();
                        writer.Line("static " + name + " global::System.IParsable<" + name + ">.Parse(string s, global::System.IFormatProvider? provider) => Parse(s);");
                        writer.Line();
                        writer.Line("static bool global::System.IParsable<" + name + ">.TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, global::System.IFormatProvider? provider, out " + name + " result) => TryParse(s, out result);");
                    }
                }

                if (definition.SystemTextJsonAvailable)
                {
                    writer.Line();
                    EmitSystemTextJsonConverter(writer, definition);
                }
            }
        }

        return writer.ToString();
    }

    // ------------------------------------------------------------------ shared pieces

    /// <summary>
    /// Parse and TryParse are only generated for values that can be parsed, so the prefix's
    /// documentation must not promise them when they are absent.
    /// </summary>
    private static string PrefixDocComment(bool canParse)
        => canParse
            ? "/// <summary>Prefix written by ToString() and accepted (optionally) by Parse/TryParse.</summary>"
            : "/// <summary>Prefix written by ToString().</summary>";

    /// <summary>
    /// Writes ToString(). A string-valued id concatenates; anything else goes through an interpolated
    /// string pinned to the invariant culture.
    /// <para>
    /// The obvious <c>Convert.ToString(Value, CultureInfo.InvariantCulture)</c> boxes the value and then
    /// concatenates, which measured at 232 bytes an id against the record form's 104. An interpolated
    /// string handler formats a Guid or a number straight into the buffer, so nothing is boxed and one
    /// string comes out. See docs/performance.md.
    /// </para>
    /// </summary>
    private static void EmitToString(CodeWriter writer, ValueTypeInfo value)
    {
        writer.Line("public override string ToString()");

        if (value.IsString)
        {
            // A null value is possible on a default struct, and the empty-prefix form has to stay
            // byte-for-byte what it was: "SKU_" for a default id, not "SKU_" plus a null.
            writer.Line("    => IdPrefix.Length == 0 ? (Value ?? string.Empty) : IdPrefix + \"_\" + (Value ?? string.Empty);");
            return;
        }

        writer.Line("    => IdPrefix.Length == 0");
        writer.Line("        ? string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $\"{Value}\")");
        writer.Line("        : string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $\"{IdPrefix}_{Value}\");");
    }

    private static void EmitGuidFactories(CodeWriter writer, string name)
    {
        writer.Line("/// <summary>A new random id.</summary>");
        writer.Line("public static " + name + " CreateUnique() => new(global::System.Guid.NewGuid());");
        writer.Directive("#if NET9_0_OR_GREATER");
        writer.Line();
        writer.Line("/// <summary>A new time-ordered (version 7) id; friendlier to database indexes than a random one.</summary>");
        writer.Line("public static " + name + " CreateSequential() => new(global::System.Guid.CreateVersion7());");
        writer.Directive("#endif");
    }

    private static void EmitParse(CodeWriter writer, EntityIdDefinition definition, bool isStruct)
    {
        var name = definition.Type.Name;
        var value = definition.Value;
        var resultType = isStruct ? name : name + "?";
        var resultAttribute = isStruct ? string.Empty : "[global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] ";

        writer.Line("/// <summary>Parses the textual form produced by ToString(); the prefix is optional.</summary>");
        writer.Line("/// <exception cref=\"global::System.FormatException\">The input is not a valid " + name + ".</exception>");
        using (writer.Block("public static " + name + " Parse(string input)"))
        {
            writer.Line("if (TryParse(input, out var result))");
            writer.Line("{");
            writer.Line("    return result" + (isStruct ? string.Empty : "!") + ";");
            writer.Line("}");
            writer.Line();
            writer.Line("throw new global::System.FormatException($\"'{input}' is not a valid " + name + ".\");");
        }

        writer.Line();
        writer.Line("/// <summary>Parses the textual form produced by ToString(); the prefix is optional.</summary>");
        using (writer.Block("public static bool TryParse([global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? input, " + resultAttribute + "out " + resultType + " result)"))
        {
            writer.Line("result = default;");
            writer.Line("if (input is null)");
            writer.Line("{");
            writer.Line("    return false;");
            writer.Line("}");
            writer.Line();
            writer.Line("if (IdPrefix.Length != 0 && input.StartsWith(IdPrefix + \"_\", global::System.StringComparison.Ordinal))");
            writer.Line("{");
            writer.Line("    input = input.Substring(IdPrefix.Length + 1);");
            writer.Line("}");
            writer.Line();

            if (value.IsString)
            {
                writer.Line("result = new(input);");
                writer.Line("return true;");
            }
            else
            {
                writer.Line("if (!" + value.FullyQualifiedName + ".TryParse(input, global::System.Globalization.CultureInfo.InvariantCulture, out var value))");
                writer.Line("{");
                writer.Line("    return false;");
                writer.Line("}");
                writer.Line();
                writer.Line("result = new(value);");
                writer.Line("return true;");
            }
        }
    }

    private static void EmitSystemTextJsonConverter(CodeWriter writer, EntityIdDefinition definition)
    {
        var name = definition.Type.Name;
        var value = definition.Value;

        writer.Line("/// <summary>Serializes the id as its underlying value. Applied through [JsonConverter] on the type.</summary>");
        using (writer.Block("public sealed class SystemTextJsonConverter : global::System.Text.Json.Serialization.JsonConverter<" + name + ">"))
        {
            using (writer.Block("public override " + name + " Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"))
            {
                writer.Line("var value = global::System.Text.Json.JsonSerializer.Deserialize<" + value.FullyQualifiedName + ">(ref reader, options);");
                if (!value.IsValueType)
                {
                    writer.Line("if (value is null)");
                    writer.Line("{");
                    writer.Line("    throw new global::System.Text.Json.JsonException(\"Cannot convert null to " + name + ".\");");
                    writer.Line("}");
                    writer.Line();
                }

                writer.Line("return new(value);");
            }

            writer.Line();
            writer.Line("public override void Write(global::System.Text.Json.Utf8JsonWriter writer, " + name + " value, global::System.Text.Json.JsonSerializerOptions options)");
            writer.Line("    => global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value, options);");

            if (value.CanParse)
            {
                writer.Line();
                writer.Line("public override " + name + " ReadAsPropertyName(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)");
                writer.Line("    => Parse(reader.GetString() ?? throw new global::System.Text.Json.JsonException(\"Cannot convert null to " + name + ".\"));");
                writer.Line();
                writer.Line("public override void WriteAsPropertyName(global::System.Text.Json.Utf8JsonWriter writer, " + name + " value, global::System.Text.Json.JsonSerializerOptions options)");
                writer.Line("    => writer.WritePropertyName(value.ToString());");
            }
        }
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
