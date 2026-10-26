namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The shortest start of docs/tenancy.md is one line, read from the page and compiled as it stands in a project of
/// its own, with Tenancy: it has to be all there is to write, and what the page shows the switch writing has to be
/// what it writes.
/// </summary>
public class TenancySwitchDocsExampleTests
{
    private const string Page = "tenancy.md";

    private const string Heading = "The shortest start: the switch";

    /// <summary>What the page leaves out: the using of the package, in a project whose root namespace the page's names are in.</summary>
    private static GeneratorRunOutcome Run()
    {
        var examples = DocsExamples.Of(Page, Heading);
        examples.Should().ContainSingle("the shortest start is the one line");

        return GeneratorTestHost.Create("using DDDToolkit.Supporting.Tenancy;\n" + examples[0])
            .WithAssemblyName("Shop.Tenants")
            .WithTenancy()
            .RunCore();
    }

    [Fact]
    public void The_shortest_start_compiles_as_the_page_writes_it_and_nothing_is_reported()
    {
        var result = Run();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("the one line is complete: every class and id is written, and nothing is in their way");
    }

    [Fact]
    public void What_the_page_says_the_switch_writes_is_what_it_writes()
    {
        var result = Run();

        foreach (var (title, written) in new[] { ("Role.TemplateDefault.g.cs, the comment shortened", "Role.TemplateDefault."), ("RoleId.TemplateDefault.g.cs, the comment shortened", "RoleId.TemplateDefault.") })
        {
            foreach (var line in Lines(DocsExamples.Titled(Page, title)))
            {
                result.Source(written).ReplaceLineEndings("\n").Should().Contain(line, "the page shows {0} as it is written", title);
            }
        }
    }

    private static IEnumerable<string> Lines(string code)
        => code.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && line is not "{" and not "}");
}
