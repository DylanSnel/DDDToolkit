using System.Reflection;
using System.Xml.Linq;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// The Postgres package stands on the storage package and the toolkit's Postgres integration, and on nothing
/// else: no dispatcher, no Tenancy, and no Npgsql of its own, so the application brings whichever provider it
/// already uses.
/// </summary>
public sealed class PackageReferenceTests
{
    private static readonly string[] Unwanted = ["Mediator", "MediatR", "Tenancy", "Npgsql", "Sqlite", "SqlServer", "HotChocolate", "AspNetCore", "Supabase"];

    [Fact]
    public void The_package_references_the_storage_package_and_the_toolkits_postgres_and_nothing_else()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "Source", "DDDToolkit.Supporting.Membership.Postgres", "DDDToolkit.Supporting.Membership.Postgres.csproj"));

        project.Descendants("ProjectReference").Select(reference => Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!))
            .Should().BeEquivalentTo("DDDToolkit.Abstractions", "DDDToolkit.Supporting.Membership.EntityFramework", "DDDToolkit.EntityFramework.Postgres");
        project.Descendants("PackageReference").Should().BeEmpty("it talks to Postgres through the connection the application's provider opens");
    }

    [Fact]
    public void The_package_knows_no_dispatcher_no_tenancy_and_no_provider()
    {
        // Everything the assembly needs to load, to the bottom.
        var reached = new Dictionary<string, AssemblyName>(StringComparer.Ordinal);
        var pending = new Queue<AssemblyName>(typeof(MembershipPostgresChecks).Assembly.GetReferencedAssemblies());
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
    }

    [Fact]
    public void Everything_is_in_the_packages_one_namespace_and_the_contribution_is_offered_to_the_export()
    {
        var assembly = typeof(MembershipPostgresChecks).Assembly;

        assembly.GetExportedTypes().Select(type => type.Namespace).Distinct().Should().Equal("DDDToolkit.Supporting.Membership.Postgres");
        assembly.GetExportedTypes().Select(type => type.Name).Should().BeEquivalentTo("MembershipPostgresChecks", "MembershipRowAccessContribution`1");

        // Offered open, so the class an application closes over its own member class, with its rules, counts as using it.
        assembly.GetCustomAttributes<DDDToolkit.Abstractions.Attributes.RowAccessContributionAttribute>().Select(offer => offer.Contribution)
            .Should().Equal(typeof(MembershipRowAccessContribution<>));
        typeof(DocumentMembershipFunctions).BaseType.Should().Be(typeof(MembershipRowAccessContribution<DocumentShare>));
        typeof(DocumentMembershipFunctions).GetConstructor(Type.EmptyTypes).Should().NotBeNull("the export makes the class with new, before the application starts");
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
