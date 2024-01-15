using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions;
public static class GenericSyntaxProviders
{
    public static List<string> GetGenericTypeParameters(this MethodDeclarationSyntax methodDeclarationSyntax)
    {
        return methodDeclarationSyntax.TypeParameterList?.Parameters.Select(x => x.Identifier.Text).ToList() ?? [];
    }

    public static List<string> GetBaseTypeGenericTypeParameters(this TypeDeclarationSyntax typeDeclarationSyntax)
    {
        var baseTypes = typeDeclarationSyntax.BaseList?.Types ?? new SeparatedSyntaxList<BaseTypeSyntax>();
        var genericTypeArguments = new List<string>();

        foreach (var baseType in baseTypes)
        {
            var simpleBaseType = baseType as SimpleBaseTypeSyntax;
            var genericName = simpleBaseType?.Type as GenericNameSyntax;
            if (genericName != null)
            {
                foreach (var arg in genericName.TypeArgumentList.Arguments)
                {
                    genericTypeArguments.Add(arg.ToString());
                }
            }
        }

        return genericTypeArguments;
    }
}
