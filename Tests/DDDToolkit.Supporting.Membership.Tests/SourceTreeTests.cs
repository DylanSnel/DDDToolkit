using System.Text.RegularExpressions;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The three packages are laid out as the other supporting domain is: folders by concern, one type to a file
/// that is named after it, and namespaces that say nothing of the folders. Somebody who looks for a type finds
/// it by its name, and somebody who reads a folder reads one concern.
/// </summary>
public sealed partial class SourceTreeTests
{
    private const string Core = "Source/DDDToolkit.Supporting.Membership";
    private const string EntityFramework = "Source/DDDToolkit.Supporting.Membership.EntityFramework";
    private const string Postgres = "Source/DDDToolkit.Supporting.Membership.Postgres";

    /// <summary>What a package has beside its folders: what it says about its own assembly.</summary>
    private const string AssemblyInfo = "AssemblyInfo.cs";

    public static TheoryData<string> Packages => [Core, EntityFramework, Postgres];

    [Fact]
    public void Each_package_keeps_its_files_in_folders_by_concern()
    {
        FoldersOf(Core).Should().Equal(
            "Access/Callers",
            "Access/Questions",
            "Access/RequiredAccess",
            "Access/Rules",
            "Aggregates/Members",
            "Aggregates/Members/Entities",
            "Aggregates/Members/Invariants",
            "Aggregates/Members/ValueObjects",
            "Aggregates/Roles",
            "Aggregates/Roles/Invariants",
            "Aggregates/Roles/ValueObjects",
            "Refusals",
            "Resources",
            "Services",
            "UseCases/Admission",
            "UseCases/Overview",
            "UseCases/Roles");

        FoldersOf(EntityFramework).Should().Equal(
            "Access/Ports",
            "Access/Queries",
            "Access/Questions",
            "Access/Reach",
            "Mapping",
            "Registration");

        FoldersOf(Postgres).Should().Equal("Checks", "Policies", "Registration", "Sql");
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void No_file_lies_beside_the_folders_but_what_a_package_says_about_its_assembly(string package)
    {
        Repository.SourcesOf(package).Where(file => !file.Contains('/', StringComparison.Ordinal)).Should().BeSubsetOf([AssemblyInfo]);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Every_file_declares_one_type_and_is_named_after_it(string package)
    {
        var wrong = new List<string>();
        foreach (var file in Repository.SourcesOf(package).Where(file => file != AssemblyInfo))
        {
            var text = Repository.Read(package, file);
            var name = Path.GetFileNameWithoutExtension(file);

            // A type and the generic type of the same name that closes it over one more type are one thing, in one file.
            var declared = TopLevelType().Matches(text).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToList();
            var nested = NestedPublicType().Matches(text).Select(match => match.Groups["name"].Value).ToList();

            if (declared.Count != 1)
            {
                wrong.Add(file + " declares " + (declared.Count == 0 ? "no type" : string.Join(", ", declared)));
                continue;
            }

            var type = declared[0];
            string[] named = nested.Count switch
            {
                // The type itself, or its generic form beside a type of the same name.
                0 => [type, type + "OfT"],

                // A public type nested in another has a file of its own, in which the outer type is only opened again.
                1 => [nested[0], type + "." + nested[0], type + "OfT." + nested[0]],
                _ => [],
            };

            if (!named.Contains(name, StringComparer.Ordinal))
            {
                wrong.Add(file + " declares " + type + (nested.Count == 0 ? string.Empty : " with " + string.Join(", ", nested) + " nested in it"));
            }
        }

        wrong.Should().BeEmpty("a file declares one type and has its name; a public type nested in another has a file of its own, named after it");
    }

    [Fact]
    public void A_folder_says_nothing_about_a_namespace()
    {
        NamespacesOf(Core).Should().BeEquivalentTo(
            "DDDToolkit.Supporting.Membership", "DDDToolkit.Supporting.Membership.Access", "DDDToolkit.Supporting.Membership.UseCases");
        NamespacesOf(EntityFramework).Should().Equal("DDDToolkit.Supporting.Membership.EntityFramework");
        NamespacesOf(Postgres).Should().Equal("DDDToolkit.Supporting.Membership.Postgres");
    }

    /// <summary>The folders of a package that hold a source file or a resource, by their path below the package.</summary>
    private static List<string> FoldersOf(string package)
    {
        var root = Path.Combine(Repository.Root(), package);
        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(file => !file.StartsWith("obj/", StringComparison.Ordinal) && !file.StartsWith("bin/", StringComparison.Ordinal) && file.Contains('/', StringComparison.Ordinal))
            .Select(file => file[..file.LastIndexOf('/')])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static List<string> NamespacesOf(string package)
        => [.. Repository.SourcesOf(package)
            .Where(file => file != AssemblyInfo)
            .Select(file => FileScopedNamespace().Match(Repository.Read(package, file)).Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>A type declared at the top of a file, which every file of the packages declares under a file-scoped namespace.</summary>
    [GeneratedRegex(@"^(?:public|internal)\s+(?:(?:static|sealed|abstract|readonly|partial)\s+)*(?:class|interface|enum|struct|record(?:\s+struct)?)\s+(?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex TopLevelType();

    /// <summary>A public type declared inside another: one level in, as the packages indent.</summary>
    [GeneratedRegex(@"^    public\s+(?:(?:static|sealed|abstract|readonly|partial)\s+)*(?:class|interface|enum|struct|record(?:\s+struct)?)\s+(?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex NestedPublicType();

    [GeneratedRegex(@"^namespace\s+(?<name>[\w.]+);", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespace();
}
