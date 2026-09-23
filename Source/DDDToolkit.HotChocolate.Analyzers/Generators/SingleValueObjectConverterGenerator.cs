using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.HotChocolate.Analyzers;

/// <summary>
/// Generates a HotChocolate <c>IChangeTypeProvider</c> for every entity id and single value object so
/// the GraphQL runtime can convert between the object and its underlying scalar value, plus one
/// <c>Add{Module}GraphQlRuntimeBindings(this IRequestExecutorBuilder)</c> extension that binds each
/// type to a scalar (from <c>[GraphQLType&lt;T&gt;]</c> or a default mapping) and registers the converters.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SingleValueObjectConverterGenerator : IIncrementalGenerator
{
    private static readonly Dictionary<string, string> DefaultScalarTypes = new(StringComparer.Ordinal)
    {
        ["String"] = "global::HotChocolate.Types.StringType",
        ["Int16"] = "global::HotChocolate.Types.ShortType",
        ["Int32"] = "global::HotChocolate.Types.IntType",
        ["Int64"] = "global::HotChocolate.Types.LongType",
        ["Single"] = "global::HotChocolate.Types.FloatType",
        ["Double"] = "global::HotChocolate.Types.FloatType",
        ["Decimal"] = "global::HotChocolate.Types.DecimalType",
        ["Boolean"] = "global::HotChocolate.Types.BooleanType",
        ["DateTime"] = "global::HotChocolate.Types.DateTimeType",
        ["DateTimeOffset"] = "global::HotChocolate.Types.DateTimeType",
        ["DateOnly"] = "global::HotChocolate.Types.DateType",
        ["TimeOnly"] = "global::HotChocolate.Types.LocalTimeType",
        ["Guid"] = "global::HotChocolate.Types.UuidType",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entityIds = context.EntityIds()
            .Where(static definition => definition.CanGenerate)
            .Select(static (definition, _) => new BindingTarget(definition.Type, definition.Value, definition.GraphQLSchemaType, IsEntityId: true));

        var singleValueObjects = context.SingleValueObjects()
            .Where(static definition => definition.CanGenerate)
            .Select(static (definition, _) => new BindingTarget(definition.Type, definition.Value, definition.GraphQLSchemaType, IsEntityId: false));

        var targets = entityIds.Collect()
            .Combine(singleValueObjects.Collect())
            .SelectMany(static (pair, _) => pair.Left.AddRange(pair.Right));

        context.RegisterSourceOutput(targets, static (productionContext, target) => EmitChangeTypeProvider(productionContext, target));

        var registration = targets.Collect()
            .Combine(context.GetDDDOptions())
            .Combine(context.AssemblyName());

        context.RegisterSourceOutput(registration, static (productionContext, data) => EmitBindings(productionContext, data.Left.Left, data.Left.Right, data.Right));
    }

    private static void EmitChangeTypeProvider(SourceProductionContext context, BindingTarget target)
    {
        var type = target.Type;
        var valueType = target.Value.FullyQualifiedName;
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader))
            {
                writer.Line("/// <summary>Converts between the object and its underlying value for the GraphQL runtime.</summary>");
                using (writer.Block("public sealed class ChangeTypeProvider : global::HotChocolate.Utilities.IChangeTypeProvider"))
                {
                    using (writer.Block("public bool TryCreateConverter(global::System.Type source, global::System.Type target, global::HotChocolate.Utilities.ChangeTypeProvider root, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out global::HotChocolate.Utilities.ChangeType? converter)"))
                    {
                        EmitConversion(writer, type.FullyQualifiedName, valueType, "((" + type.FullyQualifiedName + ")value!).Value", "new " + type.FullyQualifiedName + "((" + valueType + ")value!)");

                        if (type.HasValidTwin)
                        {
                            writer.Line();
                            EmitConversion(writer, type.ValidTwinFullyQualifiedName, valueType, "((" + type.ValidTwinFullyQualifiedName + ")value!).Value", "new " + type.ValidTwinFullyQualifiedName + "((" + valueType + ")value!)");
                        }

                        writer.Line();
                        writer.Line("converter = null;");
                        writer.Line("return false;");
                    }
                }

                if (target.HasNodeIdSerializer)
                {
                    writer.Line();
                    EmitNodeIdValueSerializer(writer, type, target.Value);
                }
            }
        }

        context.AddSource(type.HintName(".HotChocolate"), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// The Relay node id serializer HotChocolate's own <c>AddNodeIdValueSerializerFrom&lt;T&gt;()</c> would
    /// write, written here instead. HotChocolate's generator reads the members of the type it is given, and
    /// an identifier's <c>Value</c> is generated by the toolkit: generators do not see each other's output,
    /// so it finds no member and writes a serializer that stores nothing. This one derives from
    /// HotChocolate's <c>CompositeNodeIdValueSerializer&lt;T&gt;</c> and uses its own helpers, so the node
    /// id has HotChocolate's format, <c>Order:&lt;value&gt;</c>, and any HotChocolate server or Fusion
    /// gateway reads it.
    /// </summary>
    private static void EmitNodeIdValueSerializer(CodeWriter writer, TypeDeclarationInfo type, ValueTypeInfo value)
    {
        const string Relay = "global::HotChocolate.Types.Relay.";
        var id = type.FullyQualifiedName;

        // HotChocolate declares the string overload's out value nullable; it is set whenever it returns true.
        var raw = value.IsString ? value.FullyQualifiedName + "?" : value.FullyQualifiedName;
        var rawValue = value.IsString ? "raw!" : "raw";

        writer.Line("/// <summary>Writes this identifier into a Relay node id, and reads it back out of one.</summary>");
        using (writer.Block("public sealed class NodeIdValueSerializer : " + Relay + "CompositeNodeIdValueSerializer<" + id + ">"))
        {
            using (writer.Block("protected override " + Relay + "NodeIdFormatterResult Format(global::System.Span<byte> buffer, " + id + " value, out int written)"))
            {
                writer.Line("return TryFormatIdPart(buffer, value.Value, out written)");
                writer.Line("    ? " + Relay + "NodeIdFormatterResult.Success");
                writer.Line("    : " + Relay + "NodeIdFormatterResult.BufferTooSmall;");
            }

            writer.Line();
            using (writer.Block("protected override bool TryParse(global::System.ReadOnlySpan<byte> buffer, out " + id + " value)"))
            {
                // Which serializer reads a node id is decided by the type name in front of it, so a buffer that
                // reaches this one holds this type's value.
                writer.Line("if (TryParseIdPart(buffer, out " + raw + " raw, out _))");
                writer.Line("{");
                writer.Line("    value = new " + id + "(" + rawValue + ");");
                writer.Line("    return true;");
                writer.Line("}");
                writer.Line();
                writer.Line("value = " + (type.IsStruct ? "default" : "default!") + ";");
                writer.Line("return false;");
            }
        }
    }

    private static void EmitConversion(CodeWriter writer, string objectType, string valueType, string toValue, string fromValue)
    {
        writer.Line("if (source == typeof(" + objectType + ") && target == typeof(" + valueType + "))");
        writer.Line("{");
        writer.Line("    converter = value => " + toValue + ";");
        writer.Line("    return true;");
        writer.Line("}");
        writer.Line();
        writer.Line("if (source == typeof(" + valueType + ") && target == typeof(" + objectType + "))");
        writer.Line("{");
        writer.Line("    converter = value => " + fromValue + ";");
        writer.Line("    return true;");
        writer.Line("}");
    }

    private static void EmitBindings(SourceProductionContext context, ImmutableArray<BindingTarget> targets, DDDOptions options, string? assemblyName)
    {
        if (targets.Length == 0)
        {
            return;
        }

        var moduleName = options.ResolveModuleName(assemblyName);
        var writer = new CodeWriter().Header();

        // BindRuntimeType / AddTypeConverter are extension methods; extension lookup needs these namespaces in scope.
        writer.Line("using HotChocolate;");
        writer.Line("using HotChocolate.Types;");
        writer.Line("using Microsoft.Extensions.DependencyInjection;");
        writer.Line();
        writer.Line("namespace " + Identifiers.NamespaceFrom(assemblyName) + ".GraphQl;");
        writer.Line();
        writer.Line("/// <summary>Registers the GraphQL scalar bindings and converters for the DDDToolkit types of this assembly.</summary>");
        using (writer.Block("public static class HotChocolateExtensions"))
        {
            using (writer.Block("public static global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder Add" + moduleName + "GraphQlRuntimeBindings(this global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder)"))
            {
                foreach (var target in targets.OrderBy(t => t.Type.FullyQualifiedName, StringComparer.Ordinal))
                {
                    var scalar = target.GraphQLSchemaType;
                    if (scalar is null)
                    {
                        DefaultScalarTypes.TryGetValue(target.Value.Name, out scalar);
                    }

                    if (scalar is not null)
                    {
                        writer.Line("builder.BindRuntimeType<" + target.Type.FullyQualifiedName + ", " + scalar + ">();");
                        if (target.Type.HasValidTwin)
                        {
                            writer.Line("builder.BindRuntimeType<" + target.Type.ValidTwinFullyQualifiedName + ", " + scalar + ">();");
                        }
                    }

                    writer.Line("builder.AddTypeConverter<" + target.Type.FullyQualifiedName + ".ChangeTypeProvider>();");

                    if (target.HasNodeIdSerializer)
                    {
                        writer.Line("builder.AddNodeIdValueSerializer<" + target.Type.FullyQualifiedName + ".NodeIdValueSerializer>();");
                    }
                }

                writer.Line("return builder;");
            }
        }

        context.AddSource("BindingExtensions.g.cs", SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>The raw values HotChocolate can write into a Relay node id, by CLR type name.</summary>
    private static readonly HashSet<string> NodeIdValueTypes = new(StringComparer.Ordinal) { "Guid", "String", "Int16", "Int32", "Int64" };

    private sealed record BindingTarget(TypeDeclarationInfo Type, ValueTypeInfo Value, string? GraphQLSchemaType, bool IsEntityId)
    {
        /// <summary>
        /// Whether a Relay node id can carry this type: an identifier over a value HotChocolate can write
        /// into a node id. A single value object is not an identity, so it never gets one.
        /// </summary>
        public bool HasNodeIdSerializer => IsEntityId && NodeIdValueTypes.Contains(Value.Name);
    }
}
