using System.Text.RegularExpressions;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The code of a docs page, read from the page itself, so a test compiles what a reader copies and not a copy of
/// it kept somewhere else. An example is a <c>csharp</c> block with nothing after the language: a block with a
/// title shows what a generator writes, or code shortened to its point, and is read, not compiled.
/// </summary>
internal static partial class DocsExamples
{
    /// <summary>
    /// The examples of the sections of <paramref name="page"/> whose headings are <paramref name="headings"/>, in
    /// the order the page has them. A section runs from its heading to the next heading of any level, so a
    /// section's subsections are not part of it.
    /// </summary>
    /// <param name="page">The page's file name under <c>docs/</c>.</param>
    /// <param name="headings">The headings, as the page writes them after their hashes.</param>
    public static IReadOnlyList<string> Of(string page, params string[] headings)
    {
        var examples = new List<string>();
        foreach (var heading in headings)
        {
            var section = Section(page, heading);
            var found = Example().Matches(section).Select(match => match.Groups["code"].Value).ToList();
            found.Should().NotBeEmpty("the section '{0}' of docs/{1} has an example to compile", heading, page);
            examples.AddRange(found);
        }

        return examples;
    }

    /// <summary>The code of the block titled <paramref name="title"/> in <paramref name="page"/>: what the page says a generator writes.</summary>
    public static string Titled(string page, string title)
    {
        var match = TitledBlock().Matches(Read(page)).SingleOrDefault(candidate => candidate.Groups["title"].Value == title);
        match.Should().NotBeNull("docs/{0} has a block titled '{1}'", page, title);
        return match!.Groups["code"].Value;
    }

    /// <summary>A section's text, from its heading to the next heading.</summary>
    public static string Section(string page, string heading)
    {
        var lines = Read(page).Split('\n');
        var start = Array.FindIndex(lines, line => IsHeading(line) && line.TrimStart('#').Trim() == heading);
        start.Should().BeGreaterThan(-1, "docs/{0} has the heading '{1}'", page, heading);

        return string.Join('\n', lines.Skip(start + 1).TakeWhile(line => !IsHeading(line)));
    }

    /// <summary>Whether a line is a heading: one to six hashes at its start, then a space.</summary>
    private static bool IsHeading(string line)
    {
        var hashes = line.TakeWhile(character => character == '#').Count();
        return hashes is > 0 and <= 6 && line.Length > hashes && line[hashes] == ' ';
    }

    private static string Read(string page) => File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", page)).ReplaceLineEndings("\n");

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

    [GeneratedRegex(@"^```csharp\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Example();

    [GeneratedRegex(@"^```csharp title=""(?<title>[^""]*)""\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex TitledBlock();
}
