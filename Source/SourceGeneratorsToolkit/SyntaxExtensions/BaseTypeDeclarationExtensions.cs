using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions;
public static class BaseTypeDeclarationExtensions
{
    public static string GetAccessModifier(this BaseTypeDeclarationSyntax declarationSyntax)
    {
        return declarationSyntax.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.PrivateKeyword)).Text;
    }

    public static string GetNamespace(this BaseTypeDeclarationSyntax declarationSyntax)
    {
        return declarationSyntax.Parent is BaseNamespaceDeclarationSyntax namespaceDeclaration ? namespaceDeclaration.Name.ToString() : "";
    }

    public static string GetName(this BaseTypeDeclarationSyntax declarationSyntax)
    {
        return declarationSyntax.Identifier.Text;
    }

}
