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
/// <para>
/// A module's domain and contracts projects need not reference HotChocolate, which would bring ASP.NET Core into
/// them. Their ids and single value objects then have no nested provider, so the project that builds the module's
/// schema binds them in its own <c>Add{Module}GraphQlRuntimeBindings()</c>, with
/// <c>SingleValueChangeTypeProvider&lt;T, TValue&gt;</c> and, for an identifier, <c>SingleValueNodeIdSerializer&lt;T, TValue&gt;</c>:
/// every one of the module's other projects, and the published ones of other modules. <see cref="ModuleSingleValues"/>
/// says which; a project that declares no ids of its own, such as a module's API project, still gets the method
/// for them.
/// </para>
/// <para>
/// The same method registers every struct id it binds as a key HotChocolate's paging can order a list by, with
/// <c>SingleValueCursorKeySerializer&lt;T, TValue&gt;</c>, so <c>OrderBy(x =&gt; x.Id)</c> works in front of
/// <c>ToPageAsync</c> without a line per id in the application.
/// </para>
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

        // Read off the compilation, so it runs again on every edit; the walk of each referenced assembly is cached,
        // and what comes out compares equal when nothing it names changed, so the output step stays cached.
        var referenced = context.CompilationProvider.Select(static (compilation, cancellationToken) =>
            ModuleSingleValues.Of(compilation, DefaultScalarTypes, NodeIdValueTypes, CursorKeyValueTypes, cancellationToken));

        // Whether this project can name the serializer at all: a DDDToolkit.HotChocolate from before it existed cannot.
        var registersCursorKeys = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName(ModuleSingleValues.CursorKeySerializerMetadataName) is not null);

        var registration = targets.Collect()
            .Combine(referenced)
            .Combine(registersCursorKeys)
            .Combine(context.RegistrationName())
            .Combine(context.AssemblyName())
            .Combine(context.RegistrationsOfTheSameModule(ModuleSingleValues.RegistrationClass, ModuleSingleValues.RegistrationSuffix));

        context.RegisterSourceOutput(registration, static (productionContext, data) =>
        {
            var (((((targets, referenced), registersCursorKeys), moduleName), assemblyName), sameModule) = data;
            EmitBindings(productionContext, targets, referenced, registersCursorKeys, moduleName, assemblyName, sameModule);
        });
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

    private static void EmitBindings(
        SourceProductionContext context,
        ImmutableArray<BindingTarget> targets,
        EquatableArray<ReferencedBinding> referenced,
        bool registersCursorKeys,
        string moduleName,
        string? assemblyName,
        EquatableArray<string> sameModule)
    {
        // A project with nothing of its own to bind gets no method, also when the module has bindings it could
        // call: its schema calls theirs, which has the same name, and there is only one to import.
        if (targets.Length == 0 && referenced.Count == 0)
        {
            return;
        }

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
            if (referenced.Count > 0)
            {
                writer.Line("/// <summary>");
                writer.Line("/// The ids and single value objects of this module's projects without DDDToolkit.HotChocolate, and the published");
                writer.Line("/// ones of other modules, have no converter of their own and are bound here with <c>SingleValueChangeTypeProvider</c>.");
                writer.Line("/// </summary>");
            }

            using (writer.Block("public static global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder Add" + moduleName + "GraphQlRuntimeBindings(this global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder)"))
            {
                // The other assemblies of this module name their method the same, so this one calls them: a
                // schema makes one call for the module and never imports two classes declaring one method.
                foreach (var call in sameModule)
                {
                    writer.Line(call + "(builder);");
                }

                var cursorKeys = new List<string>();

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

                    if (registersCursorKeys && target.HasCursorKeySerializer)
                    {
                        cursorKeys.Add(ModuleSingleValues.CursorKeySerializer + "<" + target.Type.FullyQualifiedName + ", " + target.Value.FullyQualifiedName + ">");
                    }
                }

                // A twin of another project's type is a type of its own here, with a binding and a provider of its
                // own: nothing nested in its parent converts it.
                foreach (var other in referenced)
                {
                    if (other.Scalar is not null)
                    {
                        writer.Line("builder.BindRuntimeType<" + other.Type + ", " + other.Scalar + ">();");
                    }

                    writer.Line("builder.AddTypeConverter<" + other.Provider + ">();");

                    if (other.NodeIdSerializer is not null)
                    {
                        writer.Line("builder.AddNodeIdValueSerializer<" + other.NodeIdSerializer + ">();");
                    }

                    if (other.CursorKeySerializer is not null)
                    {
                        cursorKeys.Add(other.CursorKeySerializer);
                    }
                }

                // Not something the schema is told: HotChocolate keeps its paging keys in one list for the process, so
                // they are registered when this method is called, once each, whichever schema or route pages first.
                if (cursorKeys.Count > 0)
                {
                    writer.Line();
                    writer.Line("// The struct ids, as keys HotChocolate's paging can order a list by: OrderBy(x => x.Id) in front of ToPageAsync.");
                    foreach (var cursorKey in cursorKeys)
                    {
                        writer.Line(cursorKey + ".Register();");
                    }

                    writer.Line();
                }

                writer.Line("return builder;");
            }
        }

        context.AddSource("BindingExtensions.g.cs", SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>The raw values HotChocolate can write into a Relay node id, by CLR type name.</summary>
    private static readonly HashSet<string> NodeIdValueTypes = new(StringComparer.Ordinal) { "Guid", "String", "Int16", "Int32", "Int64" };

    /// <summary>
    /// The raw values HotChocolate's paging has a cursor key serializer for, by CLR type name, at the oldest
    /// HotChocolate this package supports. An id over anything else is not registered as a paging key: registering
    /// it would fail when the bindings are added.
    /// </summary>
    private static readonly HashSet<string> CursorKeyValueTypes = new(StringComparer.Ordinal)
    {
        "Guid", "String", "Int16", "Int32", "Int64", "UInt16", "UInt32", "UInt64",
        "Decimal", "Double", "Single", "Boolean",
        "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly",
    };

    private sealed record BindingTarget(TypeDeclarationInfo Type, ValueTypeInfo Value, string? GraphQLSchemaType, bool IsEntityId)
    {
        /// <summary>
        /// Whether a Relay node id can carry this type: an identifier over a value HotChocolate can write
        /// into a node id. A single value object is not an identity, so it never gets one.
        /// </summary>
        public bool HasNodeIdSerializer => IsEntityId && NodeIdValueTypes.Contains(Value.Name);

        /// <summary>
        /// Whether HotChocolate's paging can order by this type: an identifier declared as a struct, which is
        /// the one the core generator makes comparable, over a value the paging can write into a cursor.
        /// </summary>
        public bool HasCursorKeySerializer => IsEntityId && Type.IsStruct && CursorKeyValueTypes.Contains(Value.Name);
    }
}
