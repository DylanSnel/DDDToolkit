using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions;
public static class PropertyDeclarationExtensions
{
    public static string GetName(this PropertyDeclarationSyntax properyDeclarationSyntax)
    {
        return properyDeclarationSyntax.Identifier.Text;
    }

    public static string GetAccessModifier(this PropertyDeclarationSyntax properyDeclarationSyntax)
    {
        return properyDeclarationSyntax.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.PrivateKeyword)).Text;
    }

}
