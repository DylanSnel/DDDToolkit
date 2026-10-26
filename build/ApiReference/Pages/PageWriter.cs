using System.Text;
using System.Xml.Linq;
using DDDToolkit.Build.ApiReference.Model;
using DDDToolkit.Build.ApiReference.Writing;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Build.ApiReference.Pages;

/// <summary>
/// Writes a laid-out page: per type its declaration, its documentation and a table of its members, then per member its
/// declaration and every tag of its documentation, in the order summary, parameters, type parameters, returns,
/// exceptions, remarks. Nothing is written that the code or its comments do not say; a member without a comment says
/// "No documentation", so the gap shows.
/// </summary>
internal sealed class PageWriter
{
    /// <summary>What the comment at the top of every page says, by which the tool knows a page it wrote.</summary>
    public const string GeneratedMarker = "by build/ApiReference; do not edit by hand";

    /// <summary>The marker an intro holds where the type parameter table of the type it names goes.</summary>
    public const string TypeParameterTable = "{type-parameters:";

    private const string Undocumented = "*No documentation.*";

    private const string PrimaryConstructor = "The primary constructor: the type's summary says what it makes.";

    private readonly Documentation _documentation;
    private readonly ReferenceLayout _layout;
    private readonly DocMarkdown _markdown;
    private string _file = "";
    private string _owner = "";

    public PageWriter(Documentation documentation, ReferenceLayout layout)
    {
        _documentation = documentation;
        _layout = layout;
        _markdown = new DocMarkdown(documentation, LinkTo);
    }

    /// <summary>Public members, types included, written without a comment, by their documentation id.</summary>
    public List<string> UndocumentedMembers { get; } = [];

    /// <summary>Types written without a comment.</summary>
    public List<string> UndocumentedTypes { get; } = [];

    /// <summary>Documented members and types with a parameter or type parameter their comment does not describe.</summary>
    public List<string> MissingTags { get; } = [];

    /// <summary>Tags naming a parameter or type parameter the member does not have: a comment gone stale.</summary>
    public List<string> StaleTags { get; } = [];

    /// <summary>The Markdown of <paramref name="page"/>.</summary>
    public string Write(PageLayout page, string intro)
    {
        _file = page.Plan.File;
        var builder = new StringBuilder();
        builder.Append("---\n")
            .Append("title: \"").Append(page.Plan.FullTitle).Append("\"\n")
            .Append("sidebar_label: ").Append(page.Plan.Title).Append('\n')
            .Append("---\n\n")
            // An HTML comment, which neither the site nor GitHub shows; a YAML comment in the front matter would read
            // as the page's title to anything that takes the first line starting with "# ", as llms.txt does.
            .Append("<!-- Generated from the packages' code and XML documentation ").Append(GeneratedMarker).Append(". ")
            .Append("Regenerate with build/generate-api-reference.sh. -->\n\n")
            .Append("# ").Append(page.Plan.FullTitle).Append("\n\n");

        if (intro.Length > 0)
        {
            builder.Append(intro).Append("\n\n");
        }

        foreach (var group in page.Groups)
        {
            builder.Append("## ").Append(group.Heading).Append("\n\n");
            if (group.Intro is not null)
            {
                builder.Append(group.Intro).Append("\n\n");
            }

            var namespaces = group.Types.Select(type => type.Type.ContainingNamespace.ToDisplayString()).Distinct().ToList();
            if (namespaces.Count == 1)
            {
                builder.Append("Namespace ").Append(DocMarkdown.CodeSpan(namespaces[0])).Append(".\n\n");
            }

            foreach (var type in group.Types)
            {
                WriteType(builder, type, namespaceLine: namespaces.Count > 1);
            }
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// The table of a generic type's type parameters, with what of the application fills each where its
    /// <c>[TemplateType]</c> says, and its constraints.
    /// </summary>
    public string TypeParameters(INamedTypeSymbol type)
    {
        var rows = new StringBuilder("| Type parameter | Filled with | Constraints |\n|---|---|---|\n");
        foreach (var parameter in type.TypeParameters)
        {
            var filledWith = "";
            if (parameter.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.Name == "TemplateTypeAttribute") is { } template
                && template.ConstructorArguments is [{ Value: INamedTypeSymbol attribute }])
            {
                var takesClass = template.NamedArguments.Any(named => named.Key == "Take" && named.Value.Value is 1);
                var declared = DocMarkdown.CodeSpan("[" + Usage(attribute) + "]");
                filledWith = takesClass
                    ? $"your {ReferenceLayout.ApplicationName(attribute)} class, declared with {declared}"
                    : $"the id it is declared with, in {declared}";
            }

            var declaration = Signatures.Declaration(type);
            var constraints = declaration.Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("where " + parameter.Name + " :", StringComparison.Ordinal));
            rows.Append("| ").Append(DocMarkdown.CodeSpan(parameter.Name)).Append(" | ").Append(filledWith).Append(" | ")
                .Append(constraints is null ? "" : DocMarkdown.CodeSpan(constraints[(constraints.IndexOf(':') + 1)..].Trim(), inTable: true))
                .Append(" |\n");
        }

        return rows.ToString().TrimEnd();
    }

    private void WriteType(StringBuilder builder, TypeLayout layout, bool namespaceLine)
    {
        var type = layout.Type;
        _markdown.CurrentType = type;

        builder.Append("### ").Append(HeadingMarkdown(layout)).Append("\n\n");
        if (layout.Template is { } template)
        {
            builder.Append("Declared in the application with ").Append(DocMarkdown.CodeSpan("[" + Usage(template) + "]")).Append(".\n\n");
        }

        builder.Append("```csharp\n").Append(Signatures.Declaration(type)).Append("\n```\n\n");
        if (namespaceLine)
        {
            builder.Append("Namespace ").Append(DocMarkdown.CodeSpan(type.ContainingNamespace.ToDisplayString())).Append(".\n\n");
        }

        var docs = _documentation.Of(type);
        _owner = type.GetDocumentationCommentId() ?? type.Name;
        if (docs is null)
        {
            UndocumentedTypes.Add(type.GetDocumentationCommentId() ?? type.ToDisplayString());
            builder.Append(Undocumented).Append("\n\n");
        }
        else
        {
            // A record's <param> tags are its primary constructor's, and the constructor shows them.
            WriteDocumentation(builder, docs, type.TypeParameters.Select(parameter => parameter.Name), parameters: null);
        }

        if (layout.Members.Count == 0)
        {
            return;
        }

        builder.Append("| Member | Summary |\n|---|---|\n");
        foreach (var member in layout.Members)
        {
            var memberDocs = _documentation.Of(member.Member);
            var name = DocMarkdown.CodeSpan(member.Anchor is null ? RowDeclaration(member) : member.Heading, inTable: true);
            var summary = IsPrimaryConstructor(member.Member, docs) ? PrimaryConstructor
                : memberDocs?.Element("summary") is { } summaryElement
                    ? member.Anchor is null ? _markdown.Cell(summaryElement) : _markdown.FirstSentence(summaryElement)
                    : memberDocs is null ? Undocumented : "";
            builder.Append("| ").Append(member.Anchor is null ? name : $"[{name}](#{member.Anchor})").Append(" | ").Append(summary).Append(" |\n");

            if (memberDocs is null)
            {
                UndocumentedMembers.Add(member.Member.GetDocumentationCommentId() ?? member.Member.ToDisplayString());
            }
        }

        builder.Append('\n');

        foreach (var member in layout.Members.Where(member => member.Anchor is not null))
        {
            builder.Append("#### ").Append(DocMarkdown.CodeSpan(member.Heading)).Append("\n\n");
            builder.Append("```csharp\n").Append(Signatures.Declaration(member.Member)).Append("\n```\n\n");

            var memberDocs = _documentation.Of(member.Member);
            if (memberDocs is null)
            {
                builder.Append(Undocumented).Append("\n\n");
                continue;
            }

            _owner = member.Member.GetDocumentationCommentId() ?? member.Member.Name;
            // A primary constructor's comment carries its type's <typeparam> tags, which the type's section shows.
            var typeParameters = member.Member switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor } => null,
                IMethodSymbol method => method.TypeParameters.Select(parameter => parameter.Name),
                _ => [],
            };
            var parameters = member.Member switch
            {
                IMethodSymbol withParameters => withParameters.Parameters.Select(parameter => parameter.Name),
                IPropertySymbol { IsIndexer: true } indexer => indexer.Parameters.Select(parameter => parameter.Name),
                _ => [],
            };

            if (IsPrimaryConstructor(member.Member, docs))
            {
                // The compiler copies the type's summary onto its primary constructor; the type's section shows it.
                memberDocs = new XElement(memberDocs);
                memberDocs.Element("summary")?.Remove();
                builder.Append(PrimaryConstructor).Append("\n\n");
            }

            WriteDocumentation(builder, memberDocs, typeParameters, parameters);
        }
    }

    private void WriteDocumentation(StringBuilder builder, XElement docs, IEnumerable<string>? typeParameters, IEnumerable<string>? parameters)
    {
        if (docs.Element("summary") is { } summary)
        {
            Paragraphs(builder, _markdown.Blocks(summary));
        }

        if (docs.Element("value") is { } value)
        {
            Paragraphs(builder, "**Value**: " + _markdown.Blocks(value));
        }

        if (parameters is not null)
        {
            Items(builder, "Parameters", parameters.ToList(), docs.Elements("param"));
        }

        if (typeParameters is not null)
        {
            Items(builder, "Type parameters", typeParameters.ToList(), docs.Elements("typeparam"));
        }

        if (docs.Element("returns") is { } returns)
        {
            Paragraphs(builder, "**Returns**: " + _markdown.Blocks(returns));
        }

        var exceptions = docs.Elements("exception").ToList();
        if (exceptions.Count > 0)
        {
            builder.Append("**Exceptions**\n\n");
            foreach (var exception in exceptions)
            {
                var cref = exception.Attribute("cref")?.Value ?? "";
                var thrown = _markdown.Blocks(new XElement("cref", new XElement("see", new XAttribute("cref", cref))));
                Item(builder, thrown, _markdown.Blocks(exception));
            }

            builder.Append('\n');
        }

        foreach (var (tag, label) in new[] { ("remarks", "Remarks"), ("example", "Example") })
        {
            foreach (var element in docs.Elements(tag))
            {
                builder.Append("**").Append(label).Append("**\n\n");
                Paragraphs(builder, _markdown.Blocks(element));
            }
        }

        var seeAlso = docs.Elements("seealso").Select(element => _markdown.Blocks(new XElement("cell", element))).Where(text => text.Length > 0).ToList();
        if (seeAlso.Count > 0)
        {
            Paragraphs(builder, "**See also**: " + string.Join(", ", seeAlso));
        }
    }

    /// <summary>
    /// The parameters or type parameters as a list, in declaration order, each with its tag; one without a tag is listed
    /// as undocumented. Tags naming no parameter, a comment gone stale, are listed after them as they are. A member that
    /// documents none of them gets no list: its signature names them, and <see cref="MissingTags"/> counts it.
    /// </summary>
    private void Items(StringBuilder builder, string label, IReadOnlyList<string> names, IEnumerable<XElement> tags)
    {
        var byName = tags.GroupBy(tag => tag.Attribute("name")?.Value ?? "").ToDictionary(group => group.Key, group => group.First());
        var missing = names.Where(name => !byName.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            MissingTags.Add($"{_owner}: {label.ToLowerInvariant()} {string.Join(", ", missing)}");
        }

        if (byName.Count == 0)
        {
            return;
        }

        builder.Append("**").Append(label).Append("**\n\n");
        foreach (var name in names)
        {
            Item(builder, DocMarkdown.CodeSpan(name), byName.TryGetValue(name, out var tag) ? _markdown.Blocks(tag) : Undocumented);
        }

        foreach (var (name, tag) in byName.Where(entry => !names.Contains(entry.Key)))
        {
            StaleTags.Add($"{_owner}: {label.ToLowerInvariant()} {name}");
            Item(builder, DocMarkdown.CodeSpan(name), _markdown.Blocks(tag));
        }

        builder.Append('\n');
    }

    /// <summary>
    /// A constructor whose summary is its type's, word for word: the primary constructor of a class or a record, whose
    /// comment the compiler builds from the type's.
    /// </summary>
    private static bool IsPrimaryConstructor(ISymbol member, XElement? typeDocs)
        => member is IMethodSymbol { MethodKind: MethodKind.Constructor }
           && typeDocs?.Element("summary") is { } typeSummary
           && member.GetDocumentationCommentXml() is { Length: > 0 } xml
           && XElement.Parse(xml, LoadOptions.PreserveWhitespace).Element("summary") is { } own
           && Normalized(own) == Normalized(typeSummary);

    private static string Normalized(XElement element)
        => System.Text.RegularExpressions.Regex.Replace(element.ToString(SaveOptions.DisableFormatting), @"s+", " ");

    private static void Item(StringBuilder builder, string name, string text)
    {
        // The item's later blocks are indented under it, or they would end the list.
        var body = text.Length == 0 ? Undocumented : text.Replace("\n", "\n  ").Replace("\n  \n", "\n\n");
        builder.Append("- ").Append(name).Append(": ").Append(body).Append('\n');
    }

    private static void Paragraphs(StringBuilder builder, string blocks)
    {
        if (blocks.Trim().Length > 0)
        {
            builder.Append(blocks.Trim()).Append("\n\n");
        }
    }

    private static string HeadingMarkdown(TypeLayout layout)
    {
        var name = DocMarkdown.CodeSpan(Signatures.Name(layout.Type));
        return layout.Template is null ? name : $"{ReferenceLayout.ApplicationName(layout.Template)} ({name})";
    }

    /// <summary>A template attribute as the application writes it: <c>SeatAggregate&lt;SeatId&gt;</c>.</summary>
    private static string Usage(INamedTypeSymbol attribute)
    {
        var name = attribute.Name.EndsWith("Attribute", StringComparison.Ordinal) ? attribute.Name[..^"Attribute".Length] : attribute.Name;
        var arguments = attribute.TypeParameters.Select(parameter => parameter.Name.StartsWith('T') ? parameter.Name[1..] : parameter.Name);
        return attribute.TypeParameters.Length == 0 ? name : name + "<" + string.Join(", ", arguments) + ">";
    }

    /// <summary>
    /// What the row of a member without a section shows: a constant as its name and value, anything else as declared,
    /// without the <c>public</c> every row would repeat.
    /// </summary>
    private static string RowDeclaration(MemberLayout member)
    {
        if (member.Member is IFieldSymbol { HasConstantValue: true } constant)
        {
            return member.Heading + " = " + ConstantText(constant);
        }

        var declaration = Signatures.Declaration(member.Member);
        return declaration.StartsWith("public ", StringComparison.Ordinal) ? declaration["public ".Length..] : declaration;
    }

    private static string ConstantText(IFieldSymbol constant)
    {
        if (constant.ContainingType.TypeKind == TypeKind.Enum)
        {
            return Convert.ToString(constant.ConstantValue, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        var declaration = Signatures.Declaration(constant);
        return declaration[(declaration.IndexOf(" = ", StringComparison.Ordinal) + 3)..];
    }

    private string? LinkTo(ISymbol symbol)
    {
        if (!_layout.Locations.TryGetValue(symbol.OriginalDefinition, out var location))
        {
            return null;
        }

        return location.File == _file ? "#" + location.Anchor : location.File + "#" + location.Anchor;
    }
}
