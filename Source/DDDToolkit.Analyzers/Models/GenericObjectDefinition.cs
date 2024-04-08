using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneratorsToolkit.Providers.Contexts;
using SourceGeneratorsToolkit.SyntaxExtensions;
using System.Collections.Generic;
using System.Linq;

namespace DDDToolkit.Analyzers.Models;
public class GenericObjectDefinition : BaseObjectDefinition
{
    public string Type { get; set; } = "";
    public Dictionary<string, TypedConstant>? Arguments { get; set; }

    public static GenericObjectDefinition FromTypeDeclarationSyntax(ResultTypeAttributeSyntaxContext ctx)
    {
        if (ctx.TargetNode is not TypeDeclarationSyntax typeSyntax)
        {
            return default!;
        }
        GenericObjectDefinition singleValueObjectDefinition = new()
        {
            Name = typeSyntax!.Identifier.Text,
            Namespace = typeSyntax.Parent is BaseNamespaceDeclarationSyntax namespaceDeclaration ? namespaceDeclaration.Name.ToString() : "",
            Type = ctx.Attributes.First(x => x.match).attribute.GenericTypes.First().Name,
            AccessModifier = typeSyntax.GetAccessModifier(),
        };
        if (singleValueObjectDefinition.Type == "String")
        {
            singleValueObjectDefinition.Type = "string";
        }

        return singleValueObjectDefinition;

    }
}
