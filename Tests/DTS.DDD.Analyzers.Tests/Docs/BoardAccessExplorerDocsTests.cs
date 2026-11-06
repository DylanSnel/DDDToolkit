using System.Text.RegularExpressions;

namespace DTS.DDD.Analyzers.Tests.Docs;

/// <summary>
/// The board access explorer the site draws on docs/membership/overview.md, held to the page, the Membership tutorial
/// and the code. Its data, website/src/components/BoardAccessExplorer/boards.json, is Harbor Works at the tutorial's
/// end: the units, the boards with their owners and members, the people with their roles in the organization, the
/// tutorial's three permissions on a board with their settings, its starter role, and the two refusals. So every name
/// in it is the tutorial's, every permission has the settings the tutorial's <c>BoardPermissions</c> gives it, the
/// boards and the grants are the ones the overview draws, who sees and who changes which board is what the tables say,
/// and every request the explorer says the tutorial sends gets the answer there that the tutorial's Try it gives. A
/// permission renamed on the page fails here until the explorer says it the same way.
/// </summary>
public partial class BoardAccessExplorerDocsTests
{
    private const string Overview = "membership/overview.md";

    private const string Tutorial = "membership/tutorial.md";

    private const string ReachFromAbove = "Reach from above: a permission held at the board's unit";

    [Fact]
    public void The_overview_marks_the_explorer_s_place_under_the_five_ways()
    {
        DocsExamples.Section(Overview, "Who holds a permission on a board")
            .Should().Contain("<div data-interactive=\"BoardAccessExplorer\">", "the explorer stands under the table of the five ways");
    }

    /// <summary>The permissions the tutorial's BoardPermissions declares: level, its arguments, the constant and the permission.</summary>
    private static List<(string Level, string Arguments, string Constant, string Name)> Declared(string tutorial)
        => [.. PermissionDeclared().Matches(tutorial)
            .Select(match => (match.Groups["level"].Value, match.Groups["arguments"].Value, match.Groups["constant"].Value, match.Groups["name"].Value))
            .DistinctBy(each => each.Item3)];

    /// <summary>The value of a setting of an attribute, <c>EveryMember = true</c> or <c>Description = "..."</c>, or null.</summary>
    private static string? Setting(string arguments, string name)
        => Regex.Match(arguments, @"\b" + name + @" = (?:""(?<value>[^""]*)""|(?<value>\w+))") is { Success: true } found ? found.Groups["value"].Value : null;

    [GeneratedRegex(@"\[(?<level>On<Board>|AtUnit|AtTenant)\((?<arguments>[^\]]*)\)\]\n\s*public const string (?<constant>\w+) = ""(?<name>[^""]+)"";")]
    private static partial Regex PermissionDeclared();

    [GeneratedRegex(@"new\(""(?<name>[^""]+)"", \[(?<permissions>BoardPermissions\.\w+(?:, BoardPermissions\.\w+)*)\]\)")]
    private static partial Regex Starter();

    [GeneratedRegex(@"\{ ""name"": ""(?<name>[^""]+)"", ""description"": ""[^""]*"", ""permissions"": \[(?<permissions>[^\]]*)\] \}")]
    private static partial Regex RoleMade();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private sealed record Unit(string Id, string Name, string? Parent);

    private sealed record OrganizationRole(string Name, bool Every, List<string> Permissions);

    private sealed record PermissionShown(string Name, string Constant, string Description, bool Owner, bool EveryMember, bool InRoles, bool FromAbove, RequestShown Request);

    private sealed record RequestShown(string Name, string Route);

    private sealed record BoardRole(string Name, List<string> Permissions);

    private sealed record Caller(string Id, string Name, string? System, string? PlacedAt, List<Grant> Grants);

    private sealed record Grant(string Role, string Unit);

    private sealed record Member(string Caller, List<string> Roles);

    private sealed record RefusalsShown(RefusalShown NotFound, RefusalShown NotPermitted);

    private sealed record RefusalShown(string Code, int Status);

    private sealed record Tried(string Caller, string Board, string Permission, string Step, int Item, int Status, string? Code);
}
