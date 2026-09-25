using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// The places a DDD diagnostic is explained keep up with the compiler: every diagnostic links to its own
/// heading on the docs site, that heading exists in docs/diagnostics.md, and the agent skill in skills/
/// knows how to fix it. A new id that is missing from any of them fails here rather than sending an IDE,
/// or an AI agent reading the build output, to a page that says nothing about it.
/// </summary>
public class DiagnosticReferenceTests
{
    private const string HelpLinkBase = "https://dylansnel.github.io/DDDToolkit/docs/diagnostics#";

    [Fact]
    public void Every_diagnostic_links_to_its_heading_on_the_docs_site()
    {
        var descriptors = Descriptors();

        descriptors.Select(descriptor => descriptor.HelpLinkUri)
            .Should().Equal(descriptors.Select(descriptor => HelpLinkBase + descriptor.Id.ToLowerInvariant()));
    }

    [Fact]
    public void Every_diagnostic_has_a_heading_of_its_own_in_the_reference_page()
    {
        // "## DDD00021" is the heading the help link's "#ddd00021" lands on, on the site and on GitHub alike.
        var headings = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "diagnostics.md"))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim());

        headings.Should().Contain(Descriptors().Select(descriptor => descriptor.Id));
    }

    [Fact]
    public void The_agent_skill_says_how_to_fix_every_diagnostic()
    {
        var reference = File.ReadAllText(Path.Combine(RepositoryRoot(), "skills", "dddtoolkit", "references", "diagnostics.md"));

        Descriptors().Select(descriptor => descriptor.Id)
            .Where(id => !reference.Contains($"## {id}", StringComparison.Ordinal))
            .Should().BeEmpty("skills/dddtoolkit/references/diagnostics.md needs a section per diagnostic");
    }

    /// <summary>
    /// Every descriptor the generators and analyzers can report. They are declared once, in the shared
    /// source compiled into each generator assembly, so the core assembly's copy is the whole list.
    /// </summary>
    private static IReadOnlyList<DiagnosticDescriptor> Descriptors()
    {
        var descriptors = typeof(DDDToolkit.Analyzers.EntityGenerator).Assembly
            .GetType("DDDToolkit.Analyzers.Common.DiagnosticDescriptors", throwOnError: true)!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .ToList();

        descriptors.Should().NotBeEmpty("an empty list would pass every check above without checking anything");
        return descriptors;
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
