using System.Xml.Linq;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// The repository has one version of HotChocolate. HotChocolate asks that all of its packages in one application
/// are the same version, so the toolkit's packages ask for one, the samples and the tests are built with that
/// one, and nothing is compiled against a HotChocolate other than the one it runs on.
/// </summary>
/// <remarks>
/// The first two tests read the project files, where the rule is written down; the third reads the assemblies
/// this run has beside it, where it either holds or does not.
/// </remarks>
public sealed class OneHotChocolateVersionTests
{
    /// <summary>The property in <c>Directory.Packages.props</c> that every package of HotChocolate takes its version from.</summary>
    private const string VersionProperty = "HotChocolateVersion";

    [Fact]
    public void Every_package_of_HotChocolate_takes_one_version_the_same_in_both_sets()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Packages.props"));

        var versions = props.Descendants("PackageVersion")
            .Select(package => (Package: (string?)package.Attribute("Include") ?? "", Version: (string?)package.Attribute("Version")))
            .Where(package => HotChocolateOfTheRun.IsHotChocolate(package.Package))
            .ToList();

        versions.Select(package => package.Package)
            .Should().Contain("HotChocolate.AspNetCore", "the scan reads what DDDToolkit.HotChocolate depends on")
            .And.Contain("HotChocolate.Fusion.Connectors.InMemory", "and Fusion, which the in-memory gateway builds on")
            .And.Contain("GreenDonut.Data.Primitives", "and GreenDonut, which is released with HotChocolate");
        versions.Where(package => package.Version != $"$({VersionProperty})")
            .Select(package => $"{package.Package}: {package.Version}")
            .Should().BeEmpty($"every package of HotChocolate takes $({VersionProperty})");

        // One property, said once for the floor and once for what the tests and samples run on, and the same in
        // both: with two values a package compiled against the floor would run on another HotChocolate in the
        // tests than the one it was compiled against.
        var properties = props.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup" && element.Name.LocalName.Contains("HotChocolate", StringComparison.OrdinalIgnoreCase))
            .ToList();

        properties.Select(property => property.Name.LocalName)
            .Should().OnlyContain(name => name == VersionProperty, "there is no second version to give a package of HotChocolate");
        properties.Select(property => (string?)property.Parent!.Attribute("Label"))
            .Should().BeEquivalentTo(["Floors", "Latest"], "each set of versions says it once");
        properties.Where(property => property.Attribute("Condition") is not null)
            .Should().BeEmpty("no project gets another HotChocolate than the rest");
        properties.Select(property => property.Value.Trim()).Distinct()
            .Should().ContainSingle("the floor and the latest are raised together");
    }

    [Fact]
    public void No_project_gives_a_package_of_HotChocolate_a_version_of_its_own()
    {
        var root = RepositoryRoot();
        var files = ProjectFiles(root).ToList();

        files.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Should().Contain("Source/DDDToolkit.HotChocolate/DDDToolkit.HotChocolate.csproj", "the scan reads the packages")
            .And.Contain("Examples/Shared/Examples.Hosting/Examples.Hosting.csproj", "and the samples")
            .And.Contain("Tests/DDDToolkit.HotChocolate.Tests/DDDToolkit.HotChocolate.Tests.csproj", "and the tests");

        var versioned = files
            .SelectMany(file => XDocument.Load(file).Descendants()
                .Where(element => element.Name.LocalName is "PackageReference" or "GlobalPackageReference")
                .Where(reference => HotChocolateOfTheRun.IsHotChocolate((string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update") ?? ""))
                .Where(reference => VersionOf(reference) is not null)
                .Select(reference => $"{Path.GetRelativePath(root, file).Replace('\\', '/')}: {(string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update")} {VersionOf(reference)}"))
            .ToList();

        versioned.Should().BeEmpty("a project names the package and Directory.Packages.props gives the version, the same to all of them");

        static string? VersionOf(XElement reference)
            => (string?)reference.Attribute("Version")
                ?? (string?)reference.Attribute("VersionOverride")
                ?? reference.Elements().FirstOrDefault(element => element.Name.LocalName is "Version" or "VersionOverride")?.Value;
    }

    [Fact]
    public void The_run_has_one_HotChocolate_and_everything_in_it_was_compiled_against_that_one()
    {
        var own = HotChocolateOfTheRun.Assemblies();
        var references = HotChocolateOfTheRun.References();

        own.Select(assembly => assembly.Name)
            .Should().Contain("HotChocolate.Types", "the run has HotChocolate beside it")
            .And.Contain("GreenDonut", "and GreenDonut, which is released with it");
        references.Select(reference => reference.Assembly)
            .Should().Contain("DDDToolkit.HotChocolate", "the package's own assembly is among what is read")
            .And.Contain("DDDToolkit.HotChocolate.Tests.Gallery.Api", "and a module's API project");

        var version = own.Select(assembly => assembly.Version).Distinct()
            .Should().ContainSingle("HotChocolate asks that all of its packages in one application are the same version").Which;
        references.Where(reference => reference.Version != version)
            .Should().BeEmpty($"what runs on HotChocolate {version} was compiled against it");
    }

    /// <summary>
    /// Every project file and every file a project imports, below the repository's root: the folders a build
    /// writes and the website's packages are not the repository's own.
    /// </summary>
    private static IEnumerable<string> ProjectFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (Path.GetExtension(file) is ".csproj" or ".props" or ".targets")
            {
                yield return file;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj" or "node_modules" || name.StartsWith('.'))
            {
                continue;
            }

            foreach (var file in ProjectFiles(child))
            {
                yield return file;
            }
        }
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
