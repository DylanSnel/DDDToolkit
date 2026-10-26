using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DDDToolkit.Build.ApiReference.Model;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Build.ApiReference.Writing;

/// <summary>
/// The tags of an XML documentation comment as Markdown: <c>&lt;para&gt;</c> a paragraph, <c>&lt;code&gt;</c> a fenced
/// block, <c>&lt;list&gt;</c> a list or a table, <c>&lt;c&gt;</c> and <c>&lt;paramref&gt;</c> inline code, and a
/// <c>&lt;see cref&gt;</c> a link to the section of what it names when the reference documents it. Text is escaped so
/// neither GitHub nor Docusaurus reads a generic's <c>&lt;T&gt;</c>, a brace or an underscore as markup.
/// </summary>
internal sealed partial class DocMarkdown(Documentation documentation, Func<ISymbol, string?> linkTo)
{
    // SQL in a <code> block is highlighted as SQL; everything else the packages show is C#.
    private static readonly string[] SqlStarts = ["create ", "select ", "alter ", "insert ", "update ", "delete ", "grant ", "revoke ", "drop ", "with ", "set ", "begin", "comment on"];

    /// <summary>The type whose members' names a <c>cref</c> to one of them shows without the type.</summary>
    public INamedTypeSymbol? CurrentType { get; set; }

    /// <summary>The Markdown blocks of <paramref name="element"/>'s content, separated by blank lines.</summary>
    public string Blocks(XElement element)
    {
        var blocks = new List<string>();
        var paragraph = new StringBuilder();

        void Flush()
        {
            // A # only means something at the start of a block, where it would make the paragraph a heading.
            var text = paragraph.ToString().Trim();
            if (text.Length > 0)
            {
                blocks.Add(text.StartsWith('#') ? "\\" + text : text);
            }

            paragraph.Clear();
        }

        foreach (var node in element.Nodes())
        {
            if (node is XElement { Name.LocalName: "para" or "code" or "list" or "remarks" or "example" or "value" } block)
            {
                Flush();
                var rendered = block.Name.LocalName switch
                {
                    "code" => Code(block),
                    "list" => List(block),
                    _ => Blocks(block),
                };
                if (rendered.Length > 0)
                {
                    blocks.Add(rendered);
                }
            }
            else
            {
                paragraph.Append(Inline(node, inTable: false));
            }
        }

        Flush();
        return string.Join("\n\n", blocks);
    }

    /// <summary>Whether <paramref name="element"/> holds only text and inline tags, so it fits in a table cell.</summary>
    public static bool IsInline(XElement element)
        => !element.Descendants().Any(descendant => descendant.Name.LocalName is "para" or "code" or "list" or "br");

    /// <summary><paramref name="element"/>'s content on one line, for a table cell; its blocks run together.</summary>
    public string Cell(XElement element)
        => Collapse(string.Concat(element.Nodes().Select(node => Inline(node, inTable: true))));

    /// <summary>The first sentence of <paramref name="element"/>'s first paragraph, for a table cell.</summary>
    public string FirstSentence(XElement element)
    {
        static bool IsBlock(XNode node) => node is XElement { Name.LocalName: "para" or "code" or "list" };

        // Nodes that already have a parent are copied into the new element, so the comment is left as it was.
        var leading = Cell(new XElement("first", element.Nodes().TakeWhile(node => !IsBlock(node))));
        if (leading.Length == 0 && element.Nodes().FirstOrDefault(IsBlock) is XElement { Name.LocalName: "para" } paragraph)
        {
            return FirstSentence(paragraph);
        }

        return Sentence(leading);
    }

    private string Inline(XNode node, bool inTable)
    {
        switch (node)
        {
            case XText text:
                return Escape(Whitespace().Replace(text.Value, " "), inTable);
            case XElement element:
                switch (element.Name.LocalName)
                {
                    case "c":
                        return CodeSpan(Whitespace().Replace(element.Value, " ").Trim(), inTable);
                    case "paramref" or "typeparamref":
                        return CodeSpan(element.Attribute("name")?.Value ?? "", inTable);
                    case "see" or "seealso":
                        return See(element, inTable);
                    case "a":
                        return Link(Inner(element, inTable), element.Attribute("href")?.Value ?? "");
                    case "b" or "strong":
                        return "**" + Inner(element, inTable).Trim() + "**";
                    case "i" or "em":
                        return "*" + Inner(element, inTable).Trim() + "*";
                    case "br":
                        return inTable ? " " : "\\\n";
                    case "para" or "code" or "list":
                        // Only in a cell, where blocks cannot be: run them together.
                        return " " + Inner(element, inTable) + " ";
                    default:
                        return Inner(element, inTable);
                }

            default:
                return "";
        }
    }

    private string Inner(XElement element, bool inTable) => string.Concat(element.Nodes().Select(node => Inline(node, inTable)));

    private string See(XElement see, bool inTable)
    {
        if (see.Attribute("langword")?.Value is { } keyword)
        {
            return CodeSpan(keyword, inTable);
        }

        if (see.Attribute("href")?.Value is { } href)
        {
            var label = Inner(see, inTable);
            return Link(label.Length > 0 ? label : Escape(href, inTable), href);
        }

        var cref = see.Attribute("cref")?.Value ?? "";
        var symbol = documentation.Resolve(cref);
        var text = see.Nodes().Any() ? null : symbol is null ? cref[(cref.IndexOf(':') + 1)..] : CrefText(symbol);
        var shown = text is null ? Inner(see, inTable) : CodeSpan(text, inTable);
        return symbol is not null && linkTo(symbol) is { } target ? Link(shown, target) : shown;
    }

    /// <summary>What a <c>cref</c> shows: a type with its type parameters, a member as <c>Type.Member</c>.</summary>
    private string CrefText(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol type)
        {
            return Signatures.Name(type.OriginalDefinition);
        }

        var name = symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } ? symbol.ContainingType.Name : symbol.Name;
        if (symbol is IMethodSymbol { IsGenericMethod: true } generic && symbol is not IMethodSymbol { MethodKind: MethodKind.Constructor })
        {
            name += "<" + string.Join(", ", generic.TypeParameters.Select(parameter => parameter.Name)) + ">";
        }

        var containing = symbol.ContainingType;
        return containing is null || SymbolEqualityComparer.Default.Equals(containing.OriginalDefinition, CurrentType?.OriginalDefinition)
            ? name
            : Signatures.Plain(containing) + "." + name;
    }

    private static string Code(XElement code)
    {
        var lines = code.Value.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            return "";
        }

        var indent = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Min(line => line.Length - line.TrimStart().Length);
        var body = string.Join("\n", lines.Select(line => line.Length >= indent ? line[indent..].TrimEnd() : line.Trim()));
        var language = code.Attribute("lang")?.Value
            ?? (SqlStarts.Any(start => body.TrimStart().StartsWith(start, StringComparison.OrdinalIgnoreCase)) ? "sql" : "csharp");
        var fence = body.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
        return fence + language + "\n" + body + "\n" + fence;
    }

    private string List(XElement list)
    {
        var type = list.Attribute("type")?.Value ?? "bullet";
        var items = list.Elements("item").ToList();

        if (type == "table")
        {
            var header = list.Element("listheader");
            var rows = new List<string>
            {
                "| " + (header is null ? "" : Cell(header.Element("term") ?? new XElement("term"))) + " | "
                + (header is null ? "" : Cell(header.Element("description") ?? new XElement("description"))) + " |",
                "|---|---|",
            };
            rows.AddRange(items.Select(item =>
                "| " + Cell(item.Element("term") ?? new XElement("term")) + " | " + Cell(item.Element("description") ?? item) + " |"));
            return string.Join("\n", rows);
        }

        var builder = new StringBuilder();
        var number = 1;
        foreach (var item in items)
        {
            var marker = type == "number" ? $"{number++}. " : "- ";
            var term = item.Element("term");
            var description = item.Element("description");
            var content = term is not null
                ? "**" + Cell(term) + "**: " + Blocks(description ?? new XElement("description"))
                : Blocks(description ?? item);

            // A continuation line of an item is indented to its text, or it would end the list.
            var indented = content.Replace("\n", "\n" + new string(' ', marker.Length)).Replace("\n" + new string(' ', marker.Length) + "\n", "\n\n");
            builder.Append(marker).Append(indented).Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    private static string Link(string text, string target) => "[" + text + "](" + target + ")";

    /// <summary>Inline code; in a table cell a <c>|</c> is escaped, which GitHub's tables read inside code too.</summary>
    public static string CodeSpan(string code, bool inTable = false)
    {
        if (inTable)
        {
            code = code.Replace("|", "\\|");
        }

        var fence = code.Contains('`') ? "``" : "`";
        var padded = code.StartsWith('`') || code.EndsWith('`') ? " " + code + " " : code;
        return fence + padded + fence;
    }

    /// <summary>Text with every character that Markdown, GitHub's or Docusaurus's, could read as markup escaped.</summary>
    public static string Escape(string text, bool inTable = false)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '\\' or '`' or '*' or '_' or '[' or ']' or '{' or '}' or '~' or '|':
                    builder.Append('\\').Append(character);
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>
    /// Up to the first full stop that ends a sentence: one followed by a space, outside inline code and a link's target.
    /// </summary>
    private static string Sentence(string markdown)
    {
        var inCode = false;
        var linkDepth = 0;
        for (var index = 0; index < markdown.Length; index++)
        {
            var character = markdown[index];
            if (character == '`')
            {
                inCode = !inCode;
            }
            else if (!inCode && character == '(' && index > 0 && markdown[index - 1] == ']')
            {
                linkDepth++;
            }
            else if (!inCode && character == ')' && linkDepth > 0)
            {
                linkDepth--;
            }
            else if (!inCode && linkDepth == 0 && character == '.' && (index + 1 == markdown.Length || markdown[index + 1] == ' ')
                     && !EndsWithAbbreviation(markdown, index))
            {
                return markdown[..(index + 1)];
            }
        }

        return markdown;
    }

    private static bool EndsWithAbbreviation(string markdown, int dot)
        => markdown[..dot].EndsWith("e.g", StringComparison.Ordinal) || markdown[..dot].EndsWith("i.e", StringComparison.Ordinal)
           || markdown[..dot].EndsWith("etc", StringComparison.Ordinal);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
