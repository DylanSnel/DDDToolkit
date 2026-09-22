using System.Collections.Immutable;
using System.Linq;
using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.EntityFramework.Analyzers;

/// <summary>
/// Generates an EF Core <c>ValueConverter</c> for every entity id and single value object (and their
/// always-valid twins), plus one <c>Add{Module}Converters(this ModelConfigurationBuilder)</c> extension
/// that registers them all as pre-convention configuration: <c>Properties&lt;T&gt;().HaveConversion(...)</c>
/// for properties and <c>DefaultTypeMapping&lt;T&gt;().HasConversion(...)</c> for everything that is not a
/// property (query parameters, constants and the element type of primitive collections). EF Core does
/// not apply the property configuration to collection elements; the DDDToolkit
/// <c>ReadOnlyCollectionConvention</c> reads the default type mapping instead.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SingleValueObjectConverterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entityIds = context.EntityIds()
            .Where(static definition => definition.CanGenerate)
            .Select(static (definition, _) => new ConverterTarget(definition.Type, definition.Value, definition.ColumnLength));

        var singleValueObjects = context.SingleValueObjects()
            .Where(static definition => definition.CanGenerate)
            .Select(static (definition, _) => new ConverterTarget(definition.Type, definition.Value, definition.ColumnLength));

        var targets = entityIds.Collect()
            .Combine(singleValueObjects.Collect())
            .SelectMany(static (pair, _) => pair.Left.AddRange(pair.Right));

        context.RegisterSourceOutput(targets, static (productionContext, target) => EmitConverter(productionContext, target));

        var registration = targets.Collect()
            .Combine(context.GetDDDOptions())
            .Combine(context.AssemblyName());

        context.RegisterSourceOutput(registration, static (productionContext, data) => EmitRegistration(productionContext, data.Left.Left, data.Left.Right, data.Right));
    }

    private static void EmitConverter(SourceProductionContext context, ConverterTarget target)
    {
        var type = target.Type;
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            EmitConverterClass(writer, type.PartialHeader, type.Name, type.FullyQualifiedName, target.Value.FullyQualifiedName);

            if (type.HasValidTwin)
            {
                writer.Line();
                EmitConverterClass(writer, "partial record " + type.ValidTwinName, type.ValidTwinName, type.ValidTwinFullyQualifiedName, target.Value.FullyQualifiedName);
            }
        }

        context.AddSource(type.HintName(".Converter"), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    private static void EmitConverterClass(CodeWriter writer, string partialHeader, string name, string fullyQualifiedName, string valueType)
    {
        using (writer.Block(partialHeader))
        {
            writer.Line("/// <summary>Stores the object as its underlying value.</summary>");
            using (writer.Block("public sealed class " + name + "Converter : global::Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<" + fullyQualifiedName + ", " + valueType + ">"))
            {
                using (writer.Block("public " + name + "Converter() : base(static v => v.Value, static v => new " + fullyQualifiedName + "(v))"))
                {
                }
            }
        }
    }

    private static void EmitRegistration(SourceProductionContext context, ImmutableArray<ConverterTarget> targets, DDDOptions options, string? assemblyName)
    {
        if (targets.Length == 0)
        {
            return;
        }

        var moduleName = options.ResolveModuleName(assemblyName);
        var writer = new CodeWriter().Header();

        writer.Line("namespace " + Identifiers.NamespaceFrom(assemblyName) + ".Converters;");
        writer.Line();
        writer.Line("/// <summary>Registers the generated EF Core value converters of this assembly.</summary>");
        using (writer.Block("public static class ConverterExtensions"))
        {
            writer.Line("/// <summary>");
            writer.Line("/// Call from <c>DbContext.ConfigureConventions</c>. Every entity id and single value object of this assembly is");
            writer.Line("/// registered twice: <c>Properties&lt;T&gt;()</c> converts properties of the type, <c>DefaultTypeMapping&lt;T&gt;()</c>");
            writer.Line("/// converts the type where no property is involved (query parameters and constants, and the element type of");
            writer.Line("/// primitive collections such as a generated <c>IReadOnlyList&lt;T&gt;</c>, which <c>AddDDDToolkitConventions()</c> maps).");
            writer.Line("/// </summary>");
            using (writer.Block("public static global::Microsoft.EntityFrameworkCore.ModelConfigurationBuilder Add" + moduleName + "Converters(this global::Microsoft.EntityFrameworkCore.ModelConfigurationBuilder modelConfigurationBuilder)"))
            {
                foreach (var target in targets.OrderBy(t => t.Type.FullyQualifiedName, System.StringComparer.Ordinal))
                {
                    EmitRegistrationLines(writer, target.Type.FullyQualifiedName, target.Type.FullyQualifiedName + "." + target.Type.Name + "Converter", target.ColumnLength);

                    if (target.Type.HasValidTwin)
                    {
                        EmitRegistrationLines(writer, target.Type.ValidTwinFullyQualifiedName, target.Type.ValidTwinFullyQualifiedName + "." + target.Type.ValidTwinName + "Converter", target.ColumnLength);
                    }
                }

                writer.Line("return modelConfigurationBuilder;");
            }
        }

        context.AddSource("ConverterExtensions.g.cs", SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    private static void EmitRegistrationLines(CodeWriter writer, string type, string converter, int columnLength)
    {
        var propertyMaxLength = columnLength > -1 ? ".HaveMaxLength(" + columnLength + ")" : string.Empty;
        var mappingMaxLength = columnLength > -1 ? ".HasMaxLength(" + columnLength + ")" : string.Empty;

        writer.Line("modelConfigurationBuilder.Properties<" + type + ">().HaveConversion<" + converter + ">()" + propertyMaxLength + ";");
        writer.Line("modelConfigurationBuilder.DefaultTypeMapping<" + type + ">().HasConversion<" + converter + ">()" + mappingMaxLength + ";");
    }

    private sealed record ConverterTarget(TypeDeclarationInfo Type, ValueTypeInfo Value, int ColumnLength);
}
