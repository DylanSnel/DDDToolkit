using System.Reflection;
using System.Xml.Linq;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The storage package stands on the core package and the toolkit's Entity Framework integration, and on
/// nothing else: no dispatcher, no Tenancy, and no database of its own, so it works on every provider an
/// application brings.
/// </summary>
public sealed class PackageReferenceTests
{
    private static readonly string[] Unwanted = ["Mediator", "MediatR", "Tenancy", "Npgsql", "Sqlite", "SqlServer", "HotChocolate", "AspNetCore"];

    [Fact]
    public void The_package_references_the_core_package_and_the_toolkits_entity_framework_and_nothing_else()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "Source", "DDDToolkit.Supporting.Membership.EntityFramework", "DDDToolkit.Supporting.Membership.EntityFramework.csproj"));

        project.Descendants("ProjectReference").Select(reference => Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/')))
            .Should().BeEquivalentTo(
                ["DDDToolkit.Supporting.Membership", "DDDToolkit.EntityFramework", "DDDToolkit.Supporting.Membership.EntityFramework.Analyzers"],
                "the core package, the toolkit's Entity Framework, and the generator it ships, which runs in the application");

        // The generator is the one place that knows of an organization kept by another package: by the names of
        // that package's types, as text. It references the compiler and nothing else.
        var generator = XDocument.Load(Path.Combine(
            RepositoryRoot(), "Source", "DDDToolkit.Supporting.Membership.EntityFramework.Analyzers", "DDDToolkit.Supporting.Membership.EntityFramework.Analyzers.csproj"));
        generator.Descendants("ProjectReference").Should().BeEmpty();
        generator.Descendants("PackageReference").Select(reference => (string)reference.Attribute("Include")!)
            .Should().BeEquivalentTo("Microsoft.CodeAnalysis.CSharp", "Microsoft.CodeAnalysis.Analyzers");
        project.Descendants("PackageReference").Select(reference => (string)reference.Attribute("Include")!)
            .Should().BeEquivalentTo(["Microsoft.EntityFrameworkCore.Relational"], "the relational layer, and whichever provider the application uses");
    }

    [Fact]
    public void The_package_knows_no_dispatcher_no_tenancy_and_no_database_of_its_own()
    {
        // Everything the assembly needs to load, to the bottom.
        var reached = new Dictionary<string, AssemblyName>(StringComparer.Ordinal);
        var pending = new Queue<AssemblyName>(typeof(MembershipModel).Assembly.GetReferencedAssemblies());
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

        reached.Keys.Should().NotContain(name => Unwanted.Any(unwanted => name.Contains(unwanted, StringComparison.OrdinalIgnoreCase)));
        reached.Keys.Where(name => name.StartsWith("DDDToolkit", StringComparison.Ordinal))
            .Should().BeEquivalentTo("DDDToolkit", "DDDToolkit.Abstractions", "DDDToolkit.EntityFramework", "DDDToolkit.Supporting.Membership");
    }

    [Fact]
    public void Everything_is_in_the_packages_one_namespace()
    {
        typeof(MembershipModel).Assembly.GetExportedTypes().Select(type => type.Namespace).Distinct()
            .Should().Equal("DDDToolkit.Supporting.Membership.EntityFramework");

        // And what is public is what an application writes against: the mapping, the registration, the queries, what works
        // from the model, and the two questions an application answers inside a statement of the context.
        typeof(MembershipModel).Assembly.GetExportedTypes().Select(type => type.Name).Should().BeEquivalentTo(
            "IPlacesReached`2",
            "IRolesWithKey`2",
            "MemberFound`1",
            "MemberKeyOn`1",
            "MemberMapping",
            "MemberQueryableExtensions",
            "MemberTableNames",
            "MembershipEntityFrameworkServiceCollectionExtensions",
            "MembershipEntityTypeBuilderExtensions",
            "MembershipModel",
            "MembershipRegistration");
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
