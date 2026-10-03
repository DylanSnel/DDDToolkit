using System.Reflection;
using System.Xml.Linq;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Postgres;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// The two supporting domains work together in an application and know nothing of each other: neither
/// references the other, and neither references a dispatcher. What joins them is written into the
/// application, by a generator of Membership's that names Tenancy's types as text. Held here, in the one
/// suite that loads both.
/// </summary>
public sealed class PackagesStayApartTests
{
    private static readonly Assembly[] MembershipPackages =
        [typeof(MembershipRules).Assembly, typeof(MembershipModel).Assembly, typeof(MembershipPostgresChecks).Assembly];

    private static readonly Assembly[] TenancyPackages =
        [typeof(TenancyRefusals).Assembly, typeof(TenancyAnswersEntityFrameworkExtensions).Assembly, typeof(TenancyPostgresChecks).Assembly];

    [Fact]
    public void The_membership_packages_reference_no_mediator_and_no_tenancy_package()
    {
        MembershipPackages.Select(package => package.GetName().Name).Should().BeEquivalentTo(
            "DDDToolkit.Supporting.Membership", "DDDToolkit.Supporting.Membership.EntityFramework", "DDDToolkit.Supporting.Membership.Postgres");

        // What the projects say they reference, the generators the packages ship included: the one that writes the
        // member list, which the core package ships, and the one that joins a resource to an organization.
        var projects = PackageProjects("DDDToolkit.Supporting.Membership*");
        projects.Select(Path.GetFileNameWithoutExtension).Should().BeEquivalentTo(
            "DDDToolkit.Supporting.Membership", "DDDToolkit.Supporting.Membership.EntityFramework", "DDDToolkit.Supporting.Membership.Postgres",
            "DDDToolkit.Supporting.Membership.Analyzers", "DDDToolkit.Supporting.Membership.EntityFramework.Analyzers");
        ReferencesOf(projects).Should().NotContain(reference => IsTenancy(reference) || IsADispatcher(reference));

        // And what the assemblies need to load, to the bottom.
        foreach (var package in MembershipPackages)
        {
            EverythingLoadedBy(package).Should().NotContain(
                name => IsTenancy(name) || IsADispatcher(name),
                "{0} works without an organization and without a dispatcher", package.GetName().Name);
        }
    }

    [Fact]
    public void The_tenancy_packages_reference_no_membership_package_and_no_mediator()
    {
        TenancyPackages.Select(package => package.GetName().Name).Should().BeEquivalentTo(
            "DDDToolkit.Supporting.Tenancy", "DDDToolkit.Supporting.Tenancy.EntityFramework", "DDDToolkit.Supporting.Tenancy.Postgres");

        var projects = PackageProjects("DDDToolkit.Supporting.Tenancy*");
        projects.Should().HaveCount(3);
        ReferencesOf(projects).Should().NotContain(reference => IsMembership(reference) || IsADispatcher(reference));

        foreach (var package in TenancyPackages)
        {
            EverythingLoadedBy(package).Should().NotContain(
                name => IsMembership(name) || IsADispatcher(name),
                "{0} knows nothing of what stands beside it in an application", package.GetName().Name);
        }

        // Nor does a file of it say the other's name.
        SourcesOf(projects).Where(source => File.ReadAllText(source).Contains("Supporting.Membership", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    [Fact]
    public void Only_memberships_generator_names_tenancy_and_it_names_it_as_text()
    {
        var generator = Path.Combine(RepositoryRoot(), "Source", "DDDToolkit.Supporting.Membership.EntityFramework.Analyzers");

        // It names the types it writes a class against by their metadata names, which is all a generator needs:
        // the application it writes into references them, and it references nothing but the compiler.
        var source = string.Concat(SourcesIn(generator).Select(File.ReadAllText));
        source.Should().Contain("\"DDDToolkit.Supporting.Tenancy\"");
        ReferencesOf(Directory.GetFiles(generator, "*.csproj")).Should().OnlyContain(reference => reference.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));

        // The three packages an application references do not say the word at all.
        foreach (var package in PackageProjects("DDDToolkit.Supporting.Membership*").Where(project => Path.GetDirectoryName(project) != generator))
        {
            SourcesIn(Path.GetDirectoryName(package)!).Where(file => File.ReadAllText(file).Contains("tenan", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Should().BeEmpty("{0} is named for members, roles and places, whoever keeps them", Path.GetFileNameWithoutExtension(package));
        }
    }

    [Fact]
    public void The_toolkits_own_generators_name_neither_package()
    {
        // The generator that closes a package's registrations over an application's classes is the toolkit's, for every
        // package: what one package needs written beside another is that package's own generator's to write.
        var root = RepositoryRoot();
        string[] own = ["DDDToolkit.Analyzers", "DDDToolkit.Analyzers.Shared", "DDDToolkit.Analyzers.CodeFixes", "DDDToolkit.EntityFramework.Analyzers"];

        own.SelectMany(project => SourcesIn(Path.Combine(root, "Source", project)))
            .Where(file => File.ReadAllText(file).Contains("DDDToolkit.Supporting.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_application_references_both_and_is_where_they_meet()
    {
        var application = typeof(CampusHost).Assembly;

        application.GetReferencedAssemblies().Select(reference => reference.Name).Should().Contain(
            ["DDDToolkit.Supporting.Membership.EntityFramework", "DDDToolkit.Supporting.Tenancy.EntityFramework"]);

        // What joins a course and a lab to the organization is in the application's own assembly, written there, and
        // internal to it: nothing of it is a type of either package.
        var written = application.GetTypes().Where(type => type.Name.EndsWith("MembershipWithTenancy`1", StringComparison.Ordinal)).ToList();
        written.Select(type => type.Name).Should().BeEquivalentTo("GeneratedCourseMembershipWithTenancy`1", "GeneratedLabMembershipWithTenancy`1");
        written.Should().OnlyContain(type => type.IsNotPublic);
        application.GetExportedTypes().Should().NotContain(type => type.Namespace != null && type.Namespace.StartsWith("DDDToolkit", StringComparison.Ordinal));
    }

    private static bool IsTenancy(string name) => name.Contains("Tenancy", StringComparison.OrdinalIgnoreCase);

    private static bool IsMembership(string name) => name.Contains("Membership", StringComparison.OrdinalIgnoreCase);

    private static bool IsADispatcher(string name) => name.Contains("Mediat", StringComparison.OrdinalIgnoreCase);

    /// <summary>The project files under Source whose folder matches <paramref name="pattern"/>.</summary>
    private static List<string> PackageProjects(string pattern)
        => [.. Directory.GetDirectories(Path.Combine(RepositoryRoot(), "Source"), pattern).SelectMany(folder => Directory.GetFiles(folder, "*.csproj"))];

    /// <summary>Every project and package the project files in <paramref name="projects"/> reference, by name.</summary>
    private static List<string> ReferencesOf(IEnumerable<string> projects)
        => [.. projects
            .SelectMany(project => XDocument.Load(project).Descendants().Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference"))
            .Select(reference => (Kind: reference.Name.LocalName, Named: (string?)reference.Attribute("Include") ?? string.Empty))
            .Select(reference => reference.Kind == "ProjectReference" ? Path.GetFileNameWithoutExtension(reference.Named.Replace('\\', '/')) : reference.Named)];

    /// <summary>Every source file of the projects in <paramref name="projects"/>, without what a build wrote.</summary>
    private static List<string> SourcesOf(IEnumerable<string> projects) => [.. projects.SelectMany(project => SourcesIn(Path.GetDirectoryName(project)!))];

    private static IEnumerable<string> SourcesIn(string folder)
        => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                           && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    /// <summary>The names of every assembly <paramref name="assembly"/> needs to load, to the bottom.</summary>
    private static HashSet<string> EverythingLoadedBy(Assembly assembly)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<AssemblyName>(assembly.GetReferencedAssemblies());
        while (pending.TryDequeue(out var name))
        {
            if (!reached.Add(name.Name!) || name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name is "netstandard" or "mscorlib")
            {
                continue;
            }

            foreach (var next in Assembly.Load(name).GetReferencedAssemblies())
            {
                pending.Enqueue(next);
            }
        }

        return reached;
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

        throw new InvalidOperationException("DDDToolkit.slnx was not found above " + AppContext.BaseDirectory + ".");
    }
}
