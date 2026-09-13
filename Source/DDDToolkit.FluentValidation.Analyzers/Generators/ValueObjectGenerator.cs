using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.FluentValidation.Analyzers.Generators;

/// <summary>
/// Gives every value object, single value object and (reference) entity id a nested partial
/// <c>Validator : AbstractValidator&lt;T&gt;</c>, an <c>Errors</c> collection and a <c>Validate()</c> override
/// that runs the validator. The user supplies the rules in the other half of the partial class:
/// <code>
/// partial class Validator { public Validator() { RuleFor(x => x.Value).EmailAddress(); } }
/// </code>
/// Struct ids are always valid by construction and get no validator.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ValueObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var valueObjects = context.ValueObjects()
            .Where(static definition => definition.CanGenerate)
            .Select(static (definition, _) => definition.Type);

        var singleValueObjects = context.SingleValueObjects()
            .Where(static definition => definition.CanGenerate)
            .Select(static (definition, _) => definition.Type);

        var entityIds = context.EntityIds()
            .Where(static definition => definition.CanGenerate && definition.Type.Kind == DeclarationKind.RecordClass)
            .Select(static (definition, _) => definition.Type);

        context.RegisterSourceOutput(valueObjects, static (productionContext, type) => Execute(productionContext, type));
        context.RegisterSourceOutput(singleValueObjects, static (productionContext, type) => Execute(productionContext, type));
        context.RegisterSourceOutput(entityIds, static (productionContext, type) => Execute(productionContext, type));
    }

    private static void Execute(SourceProductionContext context, TypeDeclarationInfo type)
    {
        var writer = new CodeWriter().Header();

        using (writer.TypeScope(type))
        {
            using (writer.Block(type.PartialHeader))
            {
                writer.Line("/// <summary>The failures produced by the last validation run; empty when valid or not yet validated.</summary>");
                writer.Line(KnownTypes.InternalAttributeUsage);
                writer.Line("[global::System.ComponentModel.DataAnnotations.Schema.NotMapped]");
                writer.Line("public global::System.Collections.ObjectModel.ReadOnlyCollection<global::FluentValidation.Results.ValidationFailure> Errors => _errors.AsReadOnly();");
                writer.Line();
                writer.Line("private global::System.Collections.Generic.List<global::FluentValidation.Results.ValidationFailure> _errors = new();");
                writer.Line();

                using (writer.Block("protected override bool Validate()"))
                {
                    writer.Line("var validator = new Validator();");
                    writer.Line("var result = validator.Validate(this);");
                    writer.Line("_errors = result.Errors;");
                    writer.Line("return result.IsValid;");
                }

                writer.Line();
                writer.Line("/// <summary>Add rules in a partial declaration of this class.</summary>");
                using (writer.Block("partial class Validator : global::FluentValidation.AbstractValidator<" + type.FullyQualifiedName + ">"))
                {
                }
            }
        }

        context.AddSource(type.HintName(".FluentValidation"), SourceText.From(writer.ToString(), Encoding.UTF8));
    }
}
