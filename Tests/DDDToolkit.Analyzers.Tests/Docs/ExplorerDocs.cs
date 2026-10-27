using System.Text.Json;
using System.Text.RegularExpressions;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// What the tests of the site's two explorers read: a component's JSON, a docs page as text, the tables and the
/// Mermaid diagrams a page draws, and one numbered item of a tutorial step's Try it list.
/// </summary>
internal static partial class ExplorerDocs
{
    /// <summary>The JSON of the site's component <paramref name="component"/>, read as <typeparamref name="T"/>.</summary>
    public static T Data<T>(string component, string file)
    {
        var path = Path.Combine(Root(), "website", "src", "components", component, file);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException(path + " is empty.");
    }

    /// <summary>A page under docs/, with its line ends as \n.</summary>
    public static string Page(string page) => File.ReadAllText(Path.Combine(Root(), "docs", page)).ReplaceLineEndings("\n");

    /// <summary>The rows of the first table under <paramref name="heading"/>, without its header and its rule, as trimmed cells.</summary>
    public static List<string[]> Table(string page, string heading)
        => [.. DocsExamples.Section(page, heading).Split('\n')
            .SkipWhile(line => !line.StartsWith('|'))
            .TakeWhile(line => line.StartsWith('|'))
            .Skip(2)
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())];

    /// <summary>The first Mermaid diagram under <paramref name="heading"/>: its nodes by id, and its lines between two nodes.</summary>
    public static Diagram DiagramOf(string page, string heading)
    {
        var section = DocsExamples.Section(page, heading);
        var code = Mermaid().Match(section);
        code.Success.Should().BeTrue("docs/{0} draws a diagram under '{1}'", page, heading);

        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var lines = new List<(string From, string To, string Kind)>();
        string? subgraph = null;
        foreach (var line in code.Groups["code"].Value.Split('\n').Select(line => line.Trim()))
        {
            if (Subgraph().Match(line) is { Success: true } opened)
            {
                subgraph = opened.Groups["title"].Value;
                continue;
            }

            if (line == "end")
            {
                subgraph = null;
                continue;
            }

            if (NodeOf().Match(line) is { Success: true } node)
            {
                nodes[node.Groups["id"].Value] = new Node(node.Groups["name"].Value, node.Groups["small"].Value, subgraph);
                continue;
            }

            foreach (var kind in new[] { "-->", "-.->", "-.-" })
            {
                var ends = line.Split(" " + kind + " ");
                if (ends.Length > 1 && ends.All(end => Id().IsMatch(end)))
                {
                    lines.AddRange(ends.Zip(ends.Skip(1), (from, to) => (from, to, kind)));
                }
            }
        }

        return new Diagram(nodes, lines);
    }

    /// <summary>
    /// The item numbered <paramref name="number"/> of the Try it list of a tutorial step, its wrapped lines joined,
    /// and the whole Try it section, which says who sends the requests when the items do not.
    /// </summary>
    public static (string Item, string Section) TryIt(string page, string step, int number)
    {
        var lines = Page(page).Split('\n');
        var start = Array.IndexOf(lines, "## " + step);
        start.Should().BeGreaterThan(-1, "docs/{0} has the step '{1}'", page, step);
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        var stepLines = lines[start..(end < 0 ? lines.Length : end)];

        var tryIt = Array.IndexOf(stepLines, "### Try it");
        tryIt.Should().BeGreaterThan(-1, "the step '{0}' of docs/{1} has a Try it list", step, page);
        var section = stepLines.Skip(tryIt + 1).TakeWhile(line => !line.StartsWith('#')).ToList();

        var first = section.FindIndex(line => line.StartsWith(number + ". ", StringComparison.Ordinal));
        first.Should().BeGreaterThan(-1, "the Try it list of '{0}' has an item {1}", step, number);
        var item = section.Skip(first + 1).TakeWhile(line => line.StartsWith(' ')).Select(line => line.Trim()).Prepend(section[first]);

        return (string.Join(' ', item), string.Join('\n', section));
    }

    /// <summary>The status the tutorial's host answers a refusal of <paramref name="kind"/> with (Tenancy tutorial, step 5).</summary>
    public static int StatusOf(RefusalKind kind) => kind switch
    {
        RefusalKind.NotFound => 404,
        RefusalKind.NotPermitted => 403,
        RefusalKind.Conflict => 409,
        _ => 400,
    };

    /// <summary>"North", "North and North Coast", "A, B and C": how the pages list names.</summary>
    public static string Listed(IReadOnlyList<string> names)
        => names.Count < 2 ? string.Concat(names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    /// <summary>Whether <paramref name="unit"/> is <paramref name="above"/> or a unit below it, in a tree of units by id.</summary>
    public static bool IsAtOrBelow(IReadOnlyDictionary<string, string?> parents, string unit, string above)
    {
        for (string? at = unit; at is not null; at = parents[at])
        {
            if (at == above)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A route of the tutorial, <c>PUT /boards/{board}/name</c>, as a pattern that finds it written in a Try it item with the id left out.</summary>
    public static Regex RouteIn(string route)
        => new("`" + Regex.Escape(route).Replace(@"\{board}", "<[^>]+>", StringComparison.Ordinal) + "`");

    /// <summary>The line a tutorial's endpoints write for a route: <c>routes.MapPut("/boards/{board}/name"</c>.</summary>
    public static string Mapped(string route)
    {
        var parts = route.Split(' ', 2);
        return "routes.Map" + parts[0][0] + parts[0][1..].ToLowerInvariant() + "(\"" + parts[1] + "\"";
    }

    public static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No DDDToolkit.slnx above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>A node of a diagram: the bold name, the small line below it, and the subgraph it is drawn in.</summary>
    public sealed record Node(string Name, string Small, string? Subgraph);

    /// <summary>A diagram's nodes by id, and its lines between two nodes with the arrow that draws them.</summary>
    public sealed record Diagram(IReadOnlyDictionary<string, Node> Nodes, IReadOnlyList<(string From, string To, string Kind)> Lines)
    {
        /// <summary>The id of the node named <paramref name="name"/>.</summary>
        public string IdOf(string name)
            => Nodes.Should().ContainSingle(node => node.Value.Name == name, "the diagram draws {0} once", name).Subject.Key;
    }

    [GeneratedRegex(@"^```mermaid\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Mermaid();

    [GeneratedRegex(@"^subgraph \w+ \[""(?<title>[^""]*)""\]$")]
    private static partial Regex Subgraph();

    [GeneratedRegex(@"^(?<id>\w+)[\[(]{1,2}""<b>(?<name>[^<]+)</b>(?:<br/><small>(?<small>.*?)</small>)?""[\])]{1,2}$")]
    private static partial Regex NodeOf();

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex Id();
}
