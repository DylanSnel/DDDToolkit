using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Generators;

/// <summary>
/// Generates the base class for <c>[Entity&lt;TId&gt;]</c> and <c>[AggregateRoot&lt;TId&gt;]</c> classes and
/// implements their get-only partial collection properties:
/// <code>
/// public partial IReadOnlyList&lt;Order&gt; Orders { get; }
/// </code>
/// becomes a private <c>List&lt;Order&gt; _orders</c> backing field plus a read-only view, annotated with
/// EF Core's <c>[BackingField]</c> when the project references Entity Framework. The entity mutates the
/// collection through the field; the outside world only ever sees the read-only interface.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.Entities(), static (productionContext, definition) => Execute(productionContext, definition));
        context.RegisterSourceOutput(context.AggregateRoots(), static (productionContext, definition) => Execute(productionContext, definition));
    }

    private static void Execute(SourceProductionContext context, EntityDefinition definition)
    {
        definition.Diagnostics.ReportAll(context);
        if (!definition.CanGenerate)
        {
            return;
        }

        var type = definition.Type;
        var baseType = definition.IsAggregateRoot ? "AggregateRoot" : "Entity";
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader + " : " + KnownTypes.BaseTypesNamespace + "." + baseType + "<" + definition.IdType + ">"))
            {
                writer.Line("/// <summary>Parameterless constructor for persistence frameworks and serializers.</summary>");
                using (writer.Block("protected " + type.Name + "()"))
                {
                }

                foreach (var collection in definition.Collections)
                {
                    writer.Line();
                    writer.Line("private readonly " + collection.BackingType + " " + collection.FieldName + " = new();");
                    writer.Line();
                    writer.Line("/// <summary>Read-only view over <see cref=\"" + collection.FieldName + "\"/>. Mutate the collection through the field.</summary>");
                    if (definition.EfBackingFieldAttributeAvailable)
                    {
                        writer.Line("[" + KnownTypes.EfBackingFieldAttributeUsage + "(nameof(" + collection.FieldName + "))]");
                    }

                    writer.Line(collection.Modifiers + " " + collection.InterfaceType + " " + collection.Name + " => " + View(collection, definition.ReadOnlySetAvailable) + ";");
                }
            }
        }

        context.AddSource(type.HintName(), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    private static string View(CollectionPropertyInfo collection, bool readOnlySetAvailable) => collection.Backing switch
    {
        CollectionBacking.HashSet when readOnlySetAvailable => "new global::System.Collections.ObjectModel.ReadOnlySet<" + collection.ElementType + ">(" + collection.FieldName + ")",
        CollectionBacking.HashSet => collection.FieldName,
        _ => collection.FieldName + ".AsReadOnly()",
    };
}
