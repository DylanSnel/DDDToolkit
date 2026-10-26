namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The example of "The owner of an aggregate" in docs/access-requirements.md, read from the page and compiled as it
/// stands: a board that marks its owner, and two requests about that owner's own. The lines the page says the generator
/// writes for them are the lines it writes.
/// </summary>
public class OwnerDocsExampleTests
{
    private const string Page = "access-requirements.md";

    /// <summary>What the page leaves out: the usings, and a namespace of the application's own.</summary>
    private const string Header =
        """
        using System;
        using DDDToolkit.Abstractions.Access;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;

        namespace Boards;

        """;

    /// <summary>What the example stands in: the ids, the module's request interface, its key and the handle of its user id.</summary>
    private const string Around =
        """
        [EntityId<Guid>] public readonly partial record struct BoardId;
        [EntityId<Guid>] public readonly partial record struct UserId;

        [AccessRequests]
        public partial interface IBoardsRequest : IRequireAccess;

        public static class BoardKeys
        {
            public const string Manage = "boards.manage";
        }

        public sealed class CallersUser : ICallersOwn<UserId>
        {
            public UserId? Find(Caller caller) => caller.UserId is { } id ? new UserId(id) : null;
        }
        """;

    [Fact]
    public void The_example_compiles_and_the_generator_writes_the_lines_the_page_shows()
    {
        var example = DocsExamples.Of(Page, "The owner of an aggregate").Should().ContainSingle().Subject;
        var result = GeneratorTestHost.Create(Header + Around, "Around.cs")
            .WithTenancy()
            .WithSource(Header + example, "Example.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("BoardsAccessBehavior.", "global::Boards.ArchiveBoard request => global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Board),");
        result.ShouldContain(
            "BoardsAccessBehavior.",
            "global::Boards.RenameBoard request => global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Board), "
            + "global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.AtTenant(\"boards.manage\")),");

        var shown = DocsExamples.Titled(Page, "the lines the generator writes into IBoardsRequest, shortened");
        shown.Should().Contain("ArchiveBoard request => AccessRequirement.OwnerOf(request.Board),")
            .And.Contain("RenameBoard request => AccessRequirement.Any(\n    AccessRequirement.OwnerOf(request.Board),\n    TenancyAccess.AtTenant(\"boards.manage\")),");
    }
}
