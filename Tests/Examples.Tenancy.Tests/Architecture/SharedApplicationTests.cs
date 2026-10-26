using System.Reflection;
using System.Xml.Linq;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Shared.Application.Paging;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// The project that holds what the modules' application projects do the same way and none of them owns: the
/// check a paged query makes of the page it is asked for. It is held to what the other shared projects are: it
/// belongs to no module and depends on none, and only the layer it is for references it.
/// </summary>
public sealed class SharedApplicationTests
{
    /// <summary>The project, beside the modules' folder.</summary>
    private const string SharedApplication = "Examples.Tenancy.Shared.Application";

    private static string ProjectFile()
        => Path.GetFullPath(Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "Shared", SharedApplication, SharedApplication + ".csproj"));

    [Fact]
    public void It_references_no_project_and_declares_no_module()
    {
        XDocument.Load(ProjectFile()).Descendants("ProjectReference").Should().BeEmpty(
            "what several modules' application projects share depends on none of them: a query hands it its sizes and its refusals");
        XDocument.Load(ProjectFile()).Descendants("PackageReference").Select(package => package.Attribute("Include")!.Value)
            .Should().Equal(["GreenDonut.Data.Primitives"], "it knows what a page is asked with, and neither storage nor HTTP");

        typeof(PageSizes).Assembly.GetName().Name.Should().Be(SharedApplication);
        typeof(PageSizes).Assembly.GetCustomAttribute<ModuleAttribute>().Should().BeNull("what belongs to no module declares none");
    }

    [Fact]
    public void Only_the_modules_application_projects_reference_it_and_every_one_of_them_does()
    {
        // By the project files, whoever names it: no domain, infrastructure or API project has it of its own, and
        // neither do the host and the UI.
        var referencing = SampleLayout.SampleProjectFiles()
            .Where(file => XDocument.Load(file).Descendants("ProjectReference")
                .Any(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')) == SharedApplication))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        referencing.Should().OnlyContain(project => SampleLayout.Holds(project!) && SampleLayout.Project(project!).Layer == Layer.Application);
        referencing.Select(project => SampleLayout.Project(project!).Module)
            .Should().BeEquivalentTo(SampleLayout.OnTheMediator.Keys, "every module pages a list, and each holds its page to its sizes through this one check");
    }
}
