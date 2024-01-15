using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions;
public static class AttributeDeclarationExtensions
{
    public static List<string> GetGenericTypeParameters(this TypeDeclarationSyntax classDeclarationSyntax)
    {
        return classDeclarationSyntax.TypeParameterList?.Parameters.Select(x => x.Identifier.Text).ToList() ?? [];
    }

    public static bool Matches<TAttribute>(this AttributeSyntax attributeSyntax)
    {
        var attributeName = attributeSyntax.Name.ToString().Replace("Attribute", "");
        var attributeTypeName = typeof(TAttribute).Name.Replace("Attribute", "");

        return attributeName == attributeTypeName;
    }

}
