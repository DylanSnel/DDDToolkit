using DDDToolkit.HotChocolate.Tests.Infrastructure;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// The sample runs on one HotChocolate. Its three modules serve their source schemas in one process, under the
/// in-memory gateway, so what each module's API project was compiled against is what the host runs it on.
/// </summary>
/// <remarks>
/// A module compiled against an older HotChocolate than the gateway brings loads without complaint and fails at
/// the first field that calls what changed in between: every paged field of the sample did, when the two differed.
/// HotChocolate asks that all of its packages in one application are the same version, and the repository gives
/// them one; this reads from the assemblies beside the test that it came out that way.
/// </remarks>
public sealed class OneHotChocolateTests
{
    [Fact]
    public void Every_module_was_compiled_against_the_HotChocolate_the_host_runs_on()
    {
        var own = HotChocolateOfTheRun.Assemblies();
        var references = HotChocolateOfTheRun.References();

        own.Select(assembly => assembly.Name)
            .Should().Contain("HotChocolate.Types.CursorPagination", "the run has HotChocolate's paging beside it")
            .And.Contain("HotChocolate.Fusion.Connectors.InMemory", "and the gateway's connector")
            .And.Contain("GreenDonut.Data.Primitives", "and what a paged query of the application takes");
        references.Select(reference => reference.Assembly).Distinct()
            .Should().Contain(SampleLayout.Projects.Where(project => project.Layer == Layer.Api).Select(project => project.Name), "every module's API project declares a schema")
            .And.Contain("Examples.Tenancy.Host", "the host composes them")
            .And.Contain("DDDToolkit.HotChocolate.Fusion.InMemory", "with the toolkit's gateway");

        var version = own.Select(assembly => assembly.Version).Distinct()
            .Should().ContainSingle("HotChocolate asks that all of its packages in one application are the same version").Which;
        references.Where(reference => reference.Version != version)
            .Should().BeEmpty($"what runs on HotChocolate {version} was compiled against it");
    }
}
