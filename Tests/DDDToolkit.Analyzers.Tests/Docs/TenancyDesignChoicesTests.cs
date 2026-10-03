using System.Text.RegularExpressions;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The section "Design choices and where to see them" of docs/tenancy.md points a reader at code, at something
/// to try and at a test for every choice. A pointer that names nothing sends the reader looking for a file that
/// was moved or a test that was renamed, so each one is followed here: a path has to exist, a test class has to
/// be declared under Tests/, and a preset has to be one the sample's try-it page lists.
/// </summary>
public partial class TenancyDesignChoicesTests
{
    private const string Heading = "## Design choices and where to see them";

    /// <summary>The folders a path written as code starts with. A link is followed whatever it starts with.</summary>
    private static readonly string[] RootFolders = ["Examples/", "Source/", "Tests/", "docs/", "skills/", ".github/"];

    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void Every_path_the_section_names_exists()
    {
        var section = Section();

        var linked = Links().Matches(section)
            .Select(match => match.Groups["target"].Value)
            .Where(target => !target.StartsWith('#') && !target.Contains("://", StringComparison.Ordinal))
            .Select(target => Path.Combine("docs", target.Split('#')[0]));
        var written = CodeSpans(section).Where(span => RootFolders.Any(folder => span.StartsWith(folder, StringComparison.Ordinal)));
        var paths = linked.Concat(written).Distinct(StringComparer.Ordinal).ToList();

        paths.Should().NotBeEmpty("the section names where each choice is in the code");
        paths.Where(path => !Exists(path)).Should().BeEmpty("every path in the section is a file or a folder of the repository");
    }

    [Fact]
    public void Every_test_the_section_names_is_declared()
    {
        var declared = TestClasses();
        var named = Rows().SelectMany(row => CodeSpans(row.Test)).Distinct(StringComparer.Ordinal).ToList();

        named.Should().NotBeEmpty("the section names a test for each choice");
        named.Where(name => !declared.Contains(name)).Should().BeEmpty("a test is named by its class, as it is declared under Tests/");
    }

    [Fact]
    public void Every_preset_the_section_names_is_one_the_try_it_page_lists()
    {
        var presets = File.ReadAllText(Path.Combine(Root, "Examples", "Tenancy", "Examples.Tenancy.Host", "DevLogin", "DevAttempts.cs"));
        var named = Rows().SelectMany(row => CodeSpans(row.Try)).Where(span => Preset().IsMatch(span)).Distinct(StringComparer.Ordinal).ToList();

        named.Should().NotBeEmpty("the section names presets to try");
        named.Where(preset => !presets.Contains($"\"{preset}\"", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public void Every_row_says_where_the_code_is_what_to_try_and_which_test_holds_it()
    {
        var rows = Rows();

        rows.Should().NotBeEmpty();
        rows.Where(row => row.Code.Length == 0 || row.Try.Length == 0 || row.Test.Length == 0)
            .Select(row => row.Choice)
            .Should().BeEmpty("a part that is not there is said in words, never left blank");
        rows.Where(row => !Links().IsMatch(row.Code) && !CodeSpans(row.Code).Any())
            .Select(row => row.Choice)
            .Should().BeEmpty("the code is named");
        rows.Where(row => !CodeSpans(row.Test).Any())
            .Select(row => row.Choice)
            .Should().BeEmpty("the test is named");
    }

    /// <summary>The section's text, from its heading to the next heading of the same level.</summary>
    private static string Section()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, "docs", "tenancy.md"));
        var start = Array.IndexOf(lines, Heading);
        start.Should().BeGreaterThan(-1, $"docs/tenancy.md has the heading '{Heading}'");

        return string.Join('\n', lines.Skip(start + 1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The rows of the section's tables, without their header and its rule. A row is the choice, and one cell that
    /// says where to see it: the code, what to try and the test, each on a line that starts with its label.
    /// </summary>
    private static List<Row> Rows()
        => [.. Section().Split('\n')
            .Where(line => line.StartsWith('|') && !line.StartsWith("|---", StringComparison.Ordinal) && !line.StartsWith("| The choice", StringComparison.Ordinal))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Select(cells =>
            {
                cells.Should().HaveCount(2, "a row is the choice and where to see it: {0}", cells[0]);

                var where = cells[1].Split("<br/>");
                where.Should().HaveCount(3, "where to see it is three lines: {0}", cells[0]);
                return new Row(cells[0], Labelled(where[0], "**Code:**"), Labelled(where[1], "**Try it:**"), Labelled(where[2], "**Test:**"));
            })];

    private static string Labelled(string line, string label)
    {
        line.Should().StartWith(label);
        return line[label.Length..].Trim();
    }

    private static IEnumerable<string> CodeSpans(string text) => CodeSpan().Matches(text).Select(match => match.Groups["code"].Value);

    /// <summary>A file, a folder, or, with a star in its last part, at least one file that matches.</summary>
    private static bool Exists(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!relative.Contains('*', StringComparison.Ordinal))
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        var folder = Path.GetDirectoryName(path)!;
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, Path.GetFileName(path)).Any();
    }

    /// <summary>Every class declared in a source file under Tests/.</summary>
    private static HashSet<string> TestClasses()
    {
        var separator = Path.DirectorySeparatorChar;

        return [.. Directory.EnumerateFiles(Path.Combine(Root, "Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal) && !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .SelectMany(file => ClassDeclaration().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups["name"].Value)];
    }

    private static string RepositoryRoot()
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

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Links();

    [GeneratedRegex("`(?<code>[^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"^[a-z]+(-[a-z0-9]+)+$")]
    private static partial Regex Preset();

    [GeneratedRegex(@"\bclass\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex ClassDeclaration();

    private sealed record Row(string Choice, string Code, string Try, string Test);
}
