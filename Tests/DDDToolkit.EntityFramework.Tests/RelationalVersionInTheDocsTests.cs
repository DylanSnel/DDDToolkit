using System.Xml.Linq;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The getting-started page tells a reader which Entity Framework to name in a project file when a provider and
/// a tool of the project ask for two: the version this repository builds with. A version on a page is text,
/// and is not raised with the repository's, so this holds the two together.
/// </summary>
public sealed class RelationalVersionInTheDocsTests
{
    [Fact]
    public void The_entity_framework_version_the_getting_started_page_names_is_the_one_the_repository_builds_with()
    {
        var root = RepositoryRoot();
        var latest = XDocument.Load(Path.Combine(root, "Directory.Packages.props"))
            .Descendants("PropertyGroup")
            .Where(group => (string?)group.Attribute("Label") == "Latest")
            .Elements("EntityFrameworkCoreVersion")
            .Should().ContainSingle().Subject.Value.Trim();

        File.ReadAllText(Path.Combine(root, "docs", "getting-started.md"))
            .Should().Contain($"<PackageReference Include=\"Microsoft.EntityFrameworkCore.Relational\" Version=\"{latest}\" />");
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
