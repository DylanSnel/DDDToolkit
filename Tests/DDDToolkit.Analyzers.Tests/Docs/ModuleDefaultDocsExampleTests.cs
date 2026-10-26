using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The example of "A module's default" in docs/access-requirements.md, read from the page and compiled as it stands: a
/// module whose interface says <c>[InTenant]</c> once, a request that takes it and two that state their own. What the
/// page says the generator writes for them is what it writes, and each requires what the page says.
/// </summary>
public class ModuleDefaultDocsExampleTests
{
    private const string Page = "access-requirements.md";

    /// <summary>What the page leaves out: the usings, and a namespace of the application's own.</summary>
    private const string Header =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.Supporting.Tenancy.Access;

        namespace Boards;

        """;

    /// <summary>What the example stands in: the board's id and the module's key.</summary>
    private const string Around =
        """
        [EntityId<Guid>] public readonly partial record struct BoardId;

        public static class BoardKeys
        {
            public const string Archive = "boards.archive";
        }
        """;

    [Fact]
    public void The_example_compiles_and_each_request_requires_what_the_page_says()
    {
        var example = DocsExamples.Of(Page, "A module's default").Should().ContainSingle().Subject;
        var result = GeneratorTestHost.Create(Header + Around, "Around.cs")
            .WithTenancy()
            .WithSource(Header + example, "Example.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("a request that states nothing in a module with a default is no DDD00077");

        var written = Written(result.Source("BoardsAccessBehavior."));
        foreach (var line in Written(DocsExamples.Titled(Page, "BoardsAccessBehavior.g.cs, with the module's default, shortened")).Split('\n')
                     .Where(line => line.Length > 0 && line is not "{" and not "}" and not "};"))
        {
            written.Should().Contain(line, "the page shows the switch as the generator writes it");
        }

        var emitted = result.Emit();
        ((IRequireAccess)emitted.New("Boards.ListBoards")).RequiredAccess.Should().Be(TenancyAccess.InTenant(), "it states nothing, and takes the default");
        ((IRequireAccess)emitted.New("Boards.ArchiveBoard", emitted.New("Boards.BoardId", Guid.NewGuid()))).RequiredAccess
            .Should().Be(TenancyAccess.ForTheWholeTenant("boards.archive"), "the key, and only the key");
        ((IRequireAccess)emitted.New("Boards.MyInvitations")).RequiredAccess.Should().Be(AccessRequirement.SignedIn(), "a signed-in user, a seat or not");
    }

    /// <summary>A generated text as the page writes it: the names without the namespaces they are written in full with, the spaces as one.</summary>
    private static string Written(string code)
    {
        foreach (var name in new[] { "Boards.", "DDDToolkit.Supporting.Tenancy.Access.", "DDDToolkit.Access." })
        {
            code = code.Replace("global::" + name, string.Empty, StringComparison.Ordinal);
        }

        code = code.Replace("DDDToolkit_Generated_IRequireAccess", "IRequireAccess", StringComparison.Ordinal);
        return string.Join('\n', code.ReplaceLineEndings("\n").Split('\n').Select(line => string.Join(' ', line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))));
    }
}
