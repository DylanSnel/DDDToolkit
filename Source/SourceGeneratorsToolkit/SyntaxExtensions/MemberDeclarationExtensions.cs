using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions;
public static class MemberDeclarationExtensions
{
    public static List<AttributeSyntax> Attributes(this MemberDeclarationSyntax memberDeclarationSyntax)
    {
        return memberDeclarationSyntax.AttributeLists.SelectMany(x => x.Attributes).ToList();
    }
}
