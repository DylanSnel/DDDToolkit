using System.Xml.Linq;

namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// The file the targets of the DDDToolkit.Analyzers package write into a project that sets <c>DDD_Module</c>, as the
/// compiler is handed it: <c>[assembly: AssemblyMetadata("DDD_Module", ...)]</c>, and what the project's
/// <c>DDD_DeclareModule</c> came to. It is read from the targets file itself, line by line, so a test compiles what
/// the build compiles, and a change to that file that the generators do not follow fails here rather than in a build.
/// </summary>
internal static class ModuleDeclarationFile
{
    private static readonly Lazy<IReadOnlyList<(string Text, string? Condition)>> Lines = new(ReadLines);

    /// <summary>The file for a project whose <c>DDD_Module</c> is <paramref name="module"/>.</summary>
    /// <param name="module">The name, which the targets trim and escape for a C# string.</param>
    /// <param name="declares">What the targets wrote for <c>DDD_DeclareModule</c>, "true" or "false", or null for nothing.</param>
    public static string For(string module, string? declares)
    {
        var name = module.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        return string.Join(
            "\n",
            Lines.Value
                .Where(line => line.Condition is null || declares is not null)
                .Select(line => line.Text
                    .Replace("$(_DDDToolkitModuleName)", name, StringComparison.Ordinal)
                    .Replace("$(_DDDToolkitDeclaresModule)", declares, StringComparison.Ordinal))) + "\n";
    }

    /// <summary>
    /// The lines the target writes, in order, each with its condition. The one condition there is says the line of
    /// <c>DDD_DeclareModule</c> is written only when there is something to say.
    /// </summary>
    private static IReadOnlyList<(string Text, string? Condition)> ReadLines()
    {
        var targets = XDocument.Load(Path.Combine(RepositoryRoot(), "Source", "DDDToolkit.Analyzers", "build", "DDDToolkit.Analyzers.targets"));
        var lines = targets.Descendants("_DDDToolkitModuleLine")
            .Where(line => line.Attribute("Include") is not null)
            .Select(line => (Text: line.Attribute("Include")!.Value, Condition: line.Attribute("Condition")?.Value))
            .ToList();

        lines.Should().NotBeEmpty("the targets write the file from _DDDToolkitModuleLine items");
        lines.Where(line => line.Condition is not null)
            .Should().OnlyContain(line => line.Condition!.Contains("_DDDToolkitDeclaresModule", StringComparison.Ordinal), "the only condition this knows is the one of DDD_DeclareModule's line");

        return lines;
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
}
