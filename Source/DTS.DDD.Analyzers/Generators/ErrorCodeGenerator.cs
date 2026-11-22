using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DTS.DDD.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DTS.DDD.Analyzers;

/// <summary>
/// Writes <c>Code</c>, <c>Message</c> and <c>Arguments</c> of an error record with <c>[ErrorCode]</c>:
/// <code>
/// [ErrorCode("stock.out-of-stock", "Only {Available} left of {Sku}.")]
/// public sealed partial record OutOfStock(string Sku, int Available) : ConflictError;
/// </code>
/// <para>
/// <c>Code</c> returns the code of the attribute. <c>Message</c> fills the placeholders of the message from the
/// properties of the record with <c>FormattableString.Invariant</c>. <c>Arguments</c> holds each public instance
/// property of the record and of its bases below <c>Error</c>, by its name.
/// </para>
/// <list type="bullet">
///   <item><description>DDD00005: the record is not <c>partial</c>.</description></item>
///   <item><description>DDD00006: the record or a type it is nested in has type parameters.</description></item>
///   <item><description>DDD00112: the type is no record class that derives from <c>Error</c>.</description></item>
///   <item><description>DDD00113: a placeholder names no property of the record.</description></item>
///   <item><description>DDD00114: two error records of the assembly have the same code.</description></item>
/// </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ErrorCodeGenerator : IIncrementalGenerator
{
    private const string AttributeName = "ErrorCode";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var errors = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.ErrorCodeAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => Describe(syntaxContext, cancellationToken));

        context.RegisterSourceOutput(errors, static (production, error) =>
        {
            error.Diagnostics.ReportAll(production);
            if (error.CanGenerate)
            {
                production.AddSource(error.Type.HintName("Error"), SourceText.From(Write(error), Encoding.UTF8));
            }
        });

        // DDD00114. Sorted by the type's name, so the error that gets the diagnostic does not depend on the order
        // the compiler handed the declarations over in.
        context.RegisterSourceOutput(errors.Collect(), static (production, all) =>
        {
            foreach (var group in all.GroupBy(static error => error.Code, StringComparer.Ordinal))
            {
                var sorted = group.OrderBy(static error => error.Type.FullyQualifiedName, StringComparer.Ordinal).ToList();
                foreach (var second in sorted.Skip(1))
                {
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.ErrorCodeTaken,
                        second.Type.Location,
                        second.Type.Name,
                        sorted[0].Type.Name,
                        second.Code).Report(production);
                }
            }
        });
    }

    private static ErrorCodeDefinition Describe(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var type = DefinitionFactory.CreateTypeInfo(symbol, (TypeDeclarationSyntax)context.TargetNode);
        var attribute = context.Attributes[0];
        var code = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty;
        var message = attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1].Value as string ?? string.Empty : string.Empty;

        var diagnostics = new List<DiagnosticInfo>();
        var canGenerate = true;

        if (!type.IsPartial)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeShouldBePartial, type.Location, type.Name, AttributeName));
            canGenerate = false;
        }

        if (type.IsGeneric)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeCannotBeGeneric, type.Location, type.Name, AttributeName));
            canGenerate = false;
        }

        if (type.Kind != DeclarationKind.RecordClass || !DerivesFromError(symbol))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ErrorCodeNotOnError, type.Location, type.Name));
            return new ErrorCodeDefinition(type, code, string.Empty, EquatableArray<string>.Empty, CanGenerate: false, diagnostics.ToEquatableArray());
        }

        var properties = PropertiesOf(symbol);
        foreach (var placeholder in Placeholders(message))
        {
            if (!properties.Contains(placeholder, StringComparer.Ordinal))
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ErrorMessagePlaceholderUnknown, type.Location, type.Name, placeholder));
                canGenerate = false;
            }
        }

        return new ErrorCodeDefinition(type, code, message, properties.ToEquatableArray(), canGenerate, diagnostics.ToEquatableArray());
    }

    private static bool DerivesFromError(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == KnownTypes.Error)
            {
                return true;
            }
        }

        return false;
    }

    // The public instance properties of the record and of its bases, up to Error, which holds Code, Message,
    // Arguments and Field. A base's properties come first, in the order of declaration.
    private static List<string> PropertiesOf(INamedTypeSymbol symbol)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var current = symbol; current is not null && current.ToDisplayString() != KnownTypes.Error; current = current.BaseType)
        {
            chain.Insert(0, current);
        }

        var names = new List<string>();
        foreach (var type in chain)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property is { IsStatic: false, IsIndexer: false, IsOverride: false, DeclaredAccessibility: Accessibility.Public }
                    && !names.Contains(property.Name))
                {
                    names.Add(property.Name);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// The property names of the placeholders in <paramref name="message"/>, such as <c>Available</c> for
    /// <c>{Available}</c> and <c>Price</c> for <c>{Price:0.00}</c>. <c>{{</c> and <c>}}</c> are literal braces. A
    /// <c>{</c> without its <c>}</c> answers the rest of the message, which names no property.
    /// </summary>
    internal static IEnumerable<string> Placeholders(string message)
    {
        for (var index = 0; index < message.Length; index++)
        {
            if (message[index] != '{')
            {
                continue;
            }

            if (index + 1 < message.Length && message[index + 1] == '{')
            {
                index++;
                continue;
            }

            var end = message.IndexOf('}', index + 1);
            if (end < 0)
            {
                yield return message.Substring(index + 1);
                yield break;
            }

            var content = message.Substring(index + 1, end - index - 1);
            var nameEnd = content.IndexOfAny([':', ',']);
            yield return (nameEnd < 0 ? content : content.Substring(0, nameEnd)).Trim();
            index = end;
        }
    }

    private static string Write(ErrorCodeDefinition error)
    {
        var writer = new CodeWriter().Header();
        using (writer.TypeScope(error.Type))
        {
            using (writer.Block(error.Type.PartialHeader))
            {
                writer.Line("/// <summary>The code from [ErrorCode]: <c>" + CodeWriter.XmlText(error.Code) + "</c>.</summary>");
                writer.Line("public override string Code => " + Literal(error.Code) + ";");
                writer.Line();
                writer.Line("/// <summary>The English message from [ErrorCode], filled in with the invariant culture.</summary>");
                writer.Line("public override string Message => global::System.FormattableString.Invariant($" + Literal(error.Message) + ");");
                writer.Line();
                writer.Line("/// <summary>The properties of the record, by name.</summary>");
                if (error.Properties.Count == 0)
                {
                    writer.Line("public override global::System.Collections.Generic.IReadOnlyDictionary<string, object?> Arguments => ArgumentsFrom(global::System.Array.Empty<global::System.Collections.Generic.KeyValuePair<string, object?>>());");
                }
                else
                {
                    writer.Line("public override global::System.Collections.Generic.IReadOnlyDictionary<string, object?> Arguments => ArgumentsFrom(new global::System.Collections.Generic.KeyValuePair<string, object?>[]");
                    writer.Line("{");
                    foreach (var property in error.Properties)
                    {
                        writer.Line("    new(" + Literal(property) + ", " + property + "),");
                    }

                    writer.Line("});");
                }
            }
        }

        return writer.ToString();
    }

    // A C# string literal of text. Braces stay as they are: inside $"..." they are the placeholders and the escapes
    // of the message.
    private static string Literal(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}

/// <summary>What <see cref="ErrorCodeGenerator"/> writes for one error record.</summary>
internal sealed record ErrorCodeDefinition(
    TypeDeclarationInfo Type,
    string Code,
    string Message,
    EquatableArray<string> Properties,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics);
