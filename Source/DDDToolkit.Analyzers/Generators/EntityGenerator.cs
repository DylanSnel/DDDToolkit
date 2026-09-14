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

                WriteInvariantSeam(writer, definition);

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

    /// <summary>
    /// Writes the invariant seam: a <c>partial void CheckInvariants()</c> the author implements in
    /// their own part of the class, and the override that runs it.
    /// <para>
    /// The seam is a <c>partial void</c> on purpose. A partial method with no implementing
    /// declaration is erased by the compiler along with every call to it, so a type that states no
    /// invariants is left with an <c>EnsureInvariants</c> whose body is a single <c>ret</c>. A shape
    /// that returns something (a list of violations, a result object) cannot do that: C# requires an
    /// implementing declaration for any partial method that is not <c>void</c>, which would force
    /// every entity in the solution to write an empty method. Reporting several violations at once
    /// is handled instead by <c>InvariantViolation(IEnumerable&lt;string&gt;)</c>, which costs nothing
    /// when it is never called.
    /// </para>
    /// </summary>
    private static void WriteInvariantSeam(CodeWriter writer, EntityDefinition definition)
    {
        var what = definition.IsAggregateRoot ? "aggregate" : "entity";

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Implement this in your own part of the class to state what must be true of this");
        writer.Line("/// " + what + " after every change, and throw (for example with <c>InvariantViolation(...)</c>)");
        writer.Line("/// when it is not. Write it without an accessibility modifier:");
        writer.Line("/// <code>partial void CheckInvariants() { ... }</code>");
        writer.Line("/// Leave it out and nothing runs: the compiler removes an unimplemented partial method");
        writer.Line("/// and every call to it.");
        writer.Line("/// </summary>");
        writer.Line("partial void CheckInvariants();");

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// Runs this type's <c>CheckInvariants()</c> seam. Called by");
        writer.Line("/// <c>DDDToolkit.EntityFramework</c> before every save that writes this " + what + ", and");
        writer.Line("/// callable directly from a test or a command handler.");
        writer.Line("/// </summary>");
        using (writer.Block("public override void EnsureInvariants()"))
        {
            writer.Line("CheckInvariants();");
        }
    }

    private static string View(CollectionPropertyInfo collection, bool readOnlySetAvailable) => collection.Backing switch
    {
        CollectionBacking.HashSet when readOnlySetAvailable => "new global::System.Collections.ObjectModel.ReadOnlySet<" + collection.ElementType + ">(" + collection.FieldName + ")",
        CollectionBacking.HashSet => collection.FieldName,
        _ => collection.FieldName + ".AsReadOnly()",
    };
}
