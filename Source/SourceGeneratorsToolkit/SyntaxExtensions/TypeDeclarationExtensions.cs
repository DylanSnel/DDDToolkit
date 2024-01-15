using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions;
public static class TypeDeclarationExtensions
{
    public static List<PropertyDeclarationSyntax> GetProperties(this TypeDeclarationSyntax declarationSyntax)
    {
        return declarationSyntax.Members.OfType<PropertyDeclarationSyntax>().ToList();
    }
    public static List<string> GetGenericTypeParameters(this TypeDeclarationSyntax classDeclarationSyntax)
    {
        return classDeclarationSyntax.TypeParameterList?.Parameters.Select(x => x.Identifier.Text).ToList() ?? [];
    }

    public static bool IsSealed(this TypeDeclarationSyntax classDeclarationSyntax)
    {
        return classDeclarationSyntax.Modifiers.Any(x => x.IsKind(SyntaxKind.SealedKeyword));
    }

    public static string SealedModifier(this TypeDeclarationSyntax classDeclarationSyntax)
    {
        return classDeclarationSyntax.IsSealed() ? "sealed " : "";
    }


}
