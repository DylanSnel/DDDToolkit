using System.Reflection;
using System.Xml.Linq;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The package stands on its own: it knows the toolkit and nothing else. No dispatcher, so an application
/// sends its requests however it likes; no Tenancy, so members can be plain users; and no storage, so a domain
/// project that declares a member class is asked for none.
/// </summary>
public sealed class PackageReferenceTests
{
    private static readonly string[] Unwanted = ["Mediator", "MediatR", "Tenancy", "EntityFramework", "Npgsql", "Sqlite", "HotChocolate", "AspNetCore"];

    [Fact]
    public void The_package_references_the_toolkit_and_nothing_else()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "Source", "DDDToolkit.Supporting.Membership", "DDDToolkit.Supporting.Membership.csproj"));

        var projects = project.Descendants("ProjectReference").Select(reference => Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/'))).ToArray();
        var packages = project.Descendants("PackageReference").Select(reference => (string)reference.Attribute("Include")!).ToArray();

        projects.Should().BeEquivalentTo(
            ["DDDToolkit", "DDDToolkit.Analyzers", "DDDToolkit.Supporting.Membership.Analyzers"],
            "the toolkit, its generator for the parent and the role name, and the generator the package ships, which runs in the application");
        packages.Should().BeEmpty();

        // That generator knows the package by the names of its types, as text. It references the compiler and nothing else.
        var generator = XDocument.Load(Path.Combine(
            RepositoryRoot(), "Source", "DDDToolkit.Supporting.Membership.Analyzers", "DDDToolkit.Supporting.Membership.Analyzers.csproj"));
        generator.Descendants("ProjectReference").Should().BeEmpty();
        generator.Descendants("PackageReference").Select(reference => (string)reference.Attribute("Include")!)
            .Should().BeEquivalentTo("Microsoft.CodeAnalysis.CSharp", "Microsoft.CodeAnalysis.Analyzers");
    }

    [Fact]
    public void The_package_knows_no_mediator_no_tenancy_and_no_entity_framework()
    {
        var assembly = typeof(MembershipRules).Assembly;

        // Everything the assembly needs to load, to the bottom.
        var reached = new Dictionary<string, AssemblyName>(StringComparer.Ordinal);
        var pending = new Queue<AssemblyName>(assembly.GetReferencedAssemblies());
        while (pending.TryDequeue(out var name))
        {
            if (!reached.TryAdd(name.Name!, name) || name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name is "netstandard" or "mscorlib")
            {
                continue;
            }

            foreach (var next in Assembly.Load(name).GetReferencedAssemblies())
            {
                pending.Enqueue(next);
            }
        }

        reached.Keys.Should().NotContain(
            name => Unwanted.Any(unwanted => name.Contains(unwanted, StringComparison.OrdinalIgnoreCase)),
            "Membership works without a dispatcher, without Tenancy and without a storage");
        reached.Keys.Where(name => name.StartsWith("DDDToolkit", StringComparison.Ordinal))
            .Should().BeEquivalentTo(["DDDToolkit", "DDDToolkit.Abstractions"]);
    }

    [Fact]
    public void No_type_of_the_package_names_a_dispatcher_tenancy_or_a_storage()
    {
        var named = typeof(MembershipRules).Assembly.GetTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(member => member switch
            {
                MethodBase method => method.GetParameters().Select(parameter => parameter.ParameterType).Append((method as MethodInfo)?.ReturnType ?? typeof(void)),
                PropertyInfo property => [property.PropertyType],
                FieldInfo field => [field.FieldType],
                _ => [],
            })
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .ToArray();

        named.Should().NotContain(@namespace => Unwanted.Any(unwanted => @namespace.Contains(unwanted, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_namespaces_are_the_packages_three()
    {
        typeof(MembershipRules).Assembly.GetExportedTypes().Select(type => type.Namespace).Distinct()
            .Should().BeEquivalentTo(
                ["DDDToolkit.Supporting.Membership", "DDDToolkit.Supporting.Membership.Access", "DDDToolkit.Supporting.Membership.UseCases"],
                "the model, the access questions and the use-case pieces, whatever folder a file is in");
    }

    [Fact]
    public void Nothing_of_membership_refers_to_tenancy_and_nothing_of_tenancy_to_membership()
    {
        // A resource beside an organization is joined to it by what the application answers, and by nothing the
        // packages know of each other: not in a package, not in the host the suites run against, and not in a
        // suite. Said of the sources as well as of the references, so a name in a comment is found too.
        var root = RepositoryRoot();
        var other = "Supporting." + "Tenancy";
        var membership = ProjectsNamed(root, "DDDToolkit.Supporting.Membership*");
        var tenancy = ProjectsNamed(root, "DDDToolkit." + other + "*");

        // One project is where the two meet: the generator that writes, into an application that has both, the
        // class that joins them. It names the other package's types, as text, and references nothing of it.
        const string Generator = "DDDToolkit.Supporting.Membership.EntityFramework.Analyzers";

        membership.Select(Path.GetFileName).Should().BeEquivalentTo(
            "DDDToolkit.Supporting.Membership", "DDDToolkit.Supporting.Membership.EntityFramework", "DDDToolkit.Supporting.Membership.Postgres",
            "DDDToolkit.Supporting.Membership.Analyzers",
            Generator,
            "DDDToolkit.Supporting.Membership.Tests", "DDDToolkit.Supporting.Membership.EntityFramework.Tests", "DDDToolkit.Supporting.Membership.Postgres.Tests",
            "DDDToolkit.Supporting.Membership.TestHost");
        tenancy.Should().NotBeEmpty("the other supporting domain is in this repository, and is what must stay apart");

        ReferencesOf(membership).Should().NotContain(
            reference => reference.Contains("Tenancy", StringComparison.OrdinalIgnoreCase),
            "no project of Membership references the other package, its generator included");

        var apart = membership.Where(project => Path.GetFileName(project) != Generator).ToList();
        SourcesOf(apart).Where(source => File.ReadAllText(source).Contains(other, StringComparison.Ordinal))
            .Should().BeEmpty("no file of Membership names the other package");

        // The packages themselves do not so much as say the word: what an application answers is named for
        // what it answers, a member, a role, a place, and not after one application of it.
        SourcesOf(apart.Where(project => project.StartsWith(Path.Combine(root, "Source"), StringComparison.Ordinal)).ToList())
            .Where(source => File.ReadAllText(source).Contains("tenan", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Should().BeEmpty("nothing in the packages is named after a tenant or its tenancy");

        ReferencesOf(tenancy.Where(project => !Path.GetFileName(project).Contains("Test", StringComparison.Ordinal)).ToList())
            .Should().NotContain(reference => reference.Contains("Membership", StringComparison.OrdinalIgnoreCase));
        SourcesOf(tenancy.Where(project => project.StartsWith(Path.Combine(root, "Source"), StringComparison.Ordinal)).ToList())
            .Where(source => File.ReadAllText(source).Contains("Supporting." + "Membership", StringComparison.Ordinal))
            .Should().BeEmpty("no file of the other package names Membership");

        // And what the host the suites run against loads, to the bottom.
        var reached = new Dictionary<string, AssemblyName>(StringComparer.Ordinal);
        var pending = new Queue<AssemblyName>(typeof(TestHost.FilingHost).Assembly.GetReferencedAssemblies());
        while (pending.TryDequeue(out var name))
        {
            if (!reached.TryAdd(name.Name!, name) || name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name is "netstandard" or "mscorlib")
            {
                continue;
            }

            foreach (var next in Assembly.Load(name).GetReferencedAssemblies())
            {
                pending.Enqueue(next);
            }
        }

        reached.Keys.Should().NotContain(name => name.Contains("Tenancy", StringComparison.OrdinalIgnoreCase) || name.Contains("Mediat", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The project folders under Source and Tests whose name matches <paramref name="pattern"/>.</summary>
    private static List<string> ProjectsNamed(string root, string pattern)
        => [.. Directory.GetDirectories(Path.Combine(root, "Source"), pattern).Concat(Directory.GetDirectories(Path.Combine(root, "Tests"), pattern))];

    /// <summary>Every project and package the projects in <paramref name="folders"/> reference, by name.</summary>
    private static List<string> ReferencesOf(List<string> folders)
        => [.. folders
            .SelectMany(folder => Directory.GetFiles(folder, "*.csproj"))
            .SelectMany(project => XDocument.Load(project).Descendants().Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference" or "Compile"))
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)];

    /// <summary>Every source file of the projects in <paramref name="folders"/>, without what a build wrote.</summary>
    private static List<string> SourcesOf(List<string> folders)
        => [.. folders
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))];

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("DDDToolkit.slnx was not found above " + AppContext.BaseDirectory + ".");
    }
}
