using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.EntityFramework.Analyzers.Generators;

/// <summary>
/// Marks every <c>[Entity]</c> (child entity, not aggregate root) as an EF Core owned type. A child
/// entity belongs to exactly one aggregate and is loaded and saved with it, which is what owned
/// types model.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.Entities().Where(static definition => definition.CanGenerate);
        context.RegisterSourceOutput(entities, static (productionContext, definition) => Execute(productionContext, definition));
    }

    private static void Execute(SourceProductionContext context, EntityDefinition definition)
    {
        var type = definition.Type;
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            writer.Line("[global::Microsoft.EntityFrameworkCore.Owned]");
            using (writer.Block(type.PartialHeader))
            {
            }
        }

        context.AddSource(type.HintName(".EntityFramework"), SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
