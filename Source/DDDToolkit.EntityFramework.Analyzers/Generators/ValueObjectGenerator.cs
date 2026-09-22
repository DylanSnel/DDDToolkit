using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.EntityFramework.Analyzers;

/// <summary>
/// Marks every <c>[ValueObject]</c> record and its always-valid twin as an EF Core complex type, so its
/// properties are stored inline in the owning entity's table.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ValueObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var valueObjects = context.ValueObjects().Where(static definition => definition.CanGenerate);
        context.RegisterSourceOutput(valueObjects, static (productionContext, definition) => Execute(productionContext, definition));
    }

    private static void Execute(SourceProductionContext context, ValueObjectDefinition definition)
    {
        var type = definition.Type;
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            writer.Line("[global::System.ComponentModel.DataAnnotations.Schema.ComplexType]");
            using (writer.Block(type.PartialHeader))
            {
            }

            if (type.HasValidTwin)
            {
                writer.Line();
                writer.Line("[global::System.ComponentModel.DataAnnotations.Schema.ComplexType]");
                using (writer.Block("partial record " + type.ValidTwinName))
                {
                }
            }
        }

        context.AddSource(type.HintName(".EntityFramework"), SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
