using System.Xml.Linq;

namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// The file the targets of the DDDToolkit.Analyzers package write into a project whose <c>DDD_Module</c> declares its
/// module, as the compiler is handed it: <c>[assembly: AssemblyMetadata("DDD_Module", ...)]</c>. It is read from the
/// targets file itself, line by line, so a test compiles what the build compiles, and a change to that file that the
/// generators do not follow fails here rather than in a build. Which projects get the file, every one that sets
/// <c>DDD_Module</c> but a test project and one that sets <c>DDD_DeclareModule</c> to false, is the target's condition,
/// which real builds prove: the module tests of DDDToolkit.Tests, and the package consumption check.
/// </summary>
internal static class ModuleDeclarationFile
{
    private static readonly Lazy<IReadOnlyList<string>> Lines = new(ReadLines);

    /// <summary>The file for a project whose <c>DDD_Module</c> is <paramref name="module"/>.</summary>
    /// <param name="module">The name, which the targets trim and escape for a C# string.</param>
    public static string For(string module)
    {
        var name = module.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        return string.Join("\n", Lines.Value.Select(line => line.Replace("$(_DDDToolkitModuleName)", name, StringComparison.Ordinal))) + "\n";
    }

    /// <summary>The lines the target writes, in order. Each is written: the target decides once whether to write the file.</summary>
    private static IReadOnlyList<string> ReadLines()
    {
        var targets = XDocument.Load(Path.Combine(RepositoryRoot(), "Source", "DDDToolkit.Analyzers", "build", "DDDToolkit.Analyzers.targets"));
        var lines = targets.Descendants("_DDDToolkitModuleLine")
            .Where(line => line.Attribute("Include") is not null)
            .ToList();

        lines.Should().NotBeEmpty("the targets write the file from _DDDToolkitModuleLine items");
        lines.Should().OnlyContain(line => line.Attribute("Condition") == null, "a line written for some projects only is one this file does not know how to write");

        return [.. lines.Select(line => line.Attribute("Include")!.Value)];
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
