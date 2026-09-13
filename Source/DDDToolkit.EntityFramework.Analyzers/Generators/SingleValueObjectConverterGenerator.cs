using System.Collections.Immutable;
using System.Linq;
using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.EntityFramework.Analyzers.Generators;

/// <summary>
/// Generates an EF Core <c>ValueConverter</c> for every entity id and single value object (and their
/// always-valid twins), plus one <c>Add{Module}Converters(this ModelConfigurationBuilder)</c> extension
/// that registers them all as pre-convention property configuration.
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
            writer.Line("/// <summary>Call from <c>DbContext.ConfigureConventions</c>.</summary>");
            using (writer.Block("public static global::Microsoft.EntityFrameworkCore.ModelConfigurationBuilder Add" + moduleName + "Converters(this global::Microsoft.EntityFrameworkCore.ModelConfigurationBuilder modelConfigurationBuilder)"))
            {
                foreach (var target in targets.OrderBy(t => t.Type.FullyQualifiedName, System.StringComparer.Ordinal))
                {
                    var maxLength = target.ColumnLength > -1 ? ".HaveMaxLength(" + target.ColumnLength + ")" : string.Empty;
                    writer.Line("modelConfigurationBuilder.Properties<" + target.Type.FullyQualifiedName + ">().HaveConversion<" + target.Type.FullyQualifiedName + "." + target.Type.Name + "Converter>()" + maxLength + ";");

                    if (target.Type.HasValidTwin)
                    {
                        writer.Line("modelConfigurationBuilder.Properties<" + target.Type.ValidTwinFullyQualifiedName + ">().HaveConversion<" + target.Type.ValidTwinFullyQualifiedName + "." + target.Type.ValidTwinName + "Converter>()" + maxLength + ";");
                    }
                }

                writer.Line("return modelConfigurationBuilder;");
            }
        }

        context.AddSource("ConverterExtensions.g.cs", SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    private sealed record ConverterTarget(TypeDeclarationInfo Type, ValueTypeInfo Value, int ColumnLength);
}
