using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Build.ApiReference.Model;

/// <summary>
/// A symbol's XML documentation as the editor shows it: the comment the compiler wrote into the package's XML file, with
/// <c>&lt;inheritdoc/&gt;</c> filled in from the member it names, or else from the member overridden or implemented.
/// The compiler leaves <c>inheritdoc</c> for tools to expand, so without this an override would read as undocumented.
/// </summary>
internal sealed class Documentation(Compilation compilation)
{
    private readonly Dictionary<ISymbol, XElement?> _cache = new(SymbolEqualityComparer.Default);

    /// <summary>The <c>&lt;member&gt;</c> element of <paramref name="symbol"/>, or <see langword="null"/> when it has none.</summary>
    public XElement? Of(ISymbol symbol) => Of(symbol.OriginalDefinition, depth: 0);

    /// <summary>The symbol a <c>cref</c> of the XML file names, such as <c>M:Namespace.Type.Method(System.String)</c>.</summary>
    public ISymbol? Resolve(string cref)
        => cref.Length > 2 && cref[1] == ':' && cref[0] != '!'
            ? DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, compilation)
            : null;

    private XElement? Of(ISymbol symbol, int depth)
    {
        if (_cache.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        var element = Parse(symbol.GetDocumentationCommentXml(expandIncludes: true));
        if (element?.Element("inheritdoc") is { } inherit && depth < 8)
        {
            inherit.Remove();
            var source = inherit.Attribute("cref")?.Value is { } cref ? Resolve(cref) : InheritedFrom(symbol);
            if (source is not null && Of(source.OriginalDefinition, depth + 1) is { } inherited)
            {
                // What the member says itself wins; the rest comes from where it inherits, one tag per name, or per
                // parameter for the tags that name one.
                foreach (var tag in inherited.Elements())
                {
                    var key = tag.Attribute("name")?.Value ?? tag.Attribute("cref")?.Value;
                    var present = element.Elements(tag.Name).Any(own => (own.Attribute("name")?.Value ?? own.Attribute("cref")?.Value) == key);
                    if (!present)
                    {
                        element.Add(new XElement(tag));
                    }
                }
            }
        }

        if (element is not null && !element.Elements().Any() && string.IsNullOrWhiteSpace(element.Value))
        {
            element = null;
        }

        _cache[symbol] = element;
        return element;
    }

    private static XElement? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var element = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        return element.Name == "member" || element.Name == "doc" ? element : new XElement("member", element);
    }

    /// <summary>The member an <c>inheritdoc</c> without a <c>cref</c> inherits from.</summary>
    private static ISymbol? InheritedFrom(ISymbol symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol { OverriddenMethod: { } overridden }:
                return overridden;
            case IPropertySymbol { OverriddenProperty: { } overridden }:
                return overridden;
            case IEventSymbol { OverriddenEvent: { } overridden }:
                return overridden;
            case INamedTypeSymbol type:
                return type.BaseType is { SpecialType: not SpecialType.System_Object } baseType ? baseType : type.Interfaces.FirstOrDefault();
            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                return constructor.ContainingType.BaseType?.Constructors.FirstOrDefault(candidate =>
                    candidate.Parameters.Select(parameter => parameter.Type).SequenceEqual(constructor.Parameters.Select(parameter => parameter.Type), SymbolEqualityComparer.Default));
        }

        var containing = symbol.ContainingType;
        if (containing is null)
        {
            return null;
        }

        foreach (var contract in containing.AllInterfaces)
        {
            foreach (var candidate in contract.GetMembers(symbol.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(containing.FindImplementationForInterfaceMember(candidate), symbol))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
