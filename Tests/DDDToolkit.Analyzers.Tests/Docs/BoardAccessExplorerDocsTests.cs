using System.Text.RegularExpressions;
using DDDToolkit.Supporting.Membership;

namespace DDDToolkit.Analyzers.Tests.Docs;

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

    private static readonly World Data = ExplorerDocs.Data<World>("BoardAccessExplorer", "boards.json");

    [Fact]
    public void The_overview_marks_the_explorer_s_place_under_the_five_ways()
    {
        DocsExamples.Section(Overview, "Who holds a permission on a board")
            .Should().Contain("<div data-interactive=\"BoardAccessExplorer\">", "the explorer stands under the table of the five ways");
    }

    [Fact]
    public void Every_name_the_explorer_shows_is_the_overview_s_or_the_tutorial_s()
    {
        var docs = ExplorerDocs.Page(Overview) + "\n" + ExplorerDocs.Page(Tutorial);

        foreach (var name in Data.Units.Select(unit => unit.Name)
                     .Concat(Data.Callers.Where(caller => caller.System is null).Select(caller => caller.Name))
                     .Concat(Data.Boards.Select(board => board.Name))
                     .Concat(Data.OrganizationRoles.Select(role => role.Name))
                     .Concat(Data.Permissions.Select(permission => permission.Request.Name)))
        {
            docs.Should().Contain(name, "the explorer shows {0}, which the overview or the tutorial names", name);
        }

        foreach (var code in Data.Permissions.Select(permission => permission.Name)
                     .Concat(Data.BoardRoles.Select(role => role.Name))
                     .Concat([Data.Refusals.NotFound.Code, Data.Refusals.NotPermitted.Code]))
        {
            docs.Should().Contain("`" + code + "`", "the explorer shows {0}, which the overview or the tutorial writes", code);
        }

        // System work in the module's scope: the overview's own example of the fifth way.
        var system = Data.Callers.Should().ContainSingle(caller => caller.System != null).Subject;
        ExplorerDocs.Page(Overview).Should().Contain("seed data written as system work in `" + system.System + "`");
    }

    [Fact]
    public void The_permissions_on_a_board_are_the_tutorial_s_with_their_settings_and_requests()
    {
        var tutorial = ExplorerDocs.Page(Tutorial);
        var declared = Declared(tutorial);
        Data.Permissions.Select(permission => permission.Name)
            .Should().BeEquivalentTo(declared.Where(each => each.Level == "On<Board>").Select(each => each.Name), "the explorer shows every permission the tutorial declares on a board");

        foreach (var permission in Data.Permissions)
        {
            var on = declared.Single(each => each.Name == permission.Name);
            var because = permission.Name;
            permission.Constant.Should().Be(on.Constant, because);
            permission.Description.Should().Be(Setting(on.Arguments, "Description"), "the explorer shows {0}'s description", because);
            permission.Owner.Should().Be(Setting(on.Arguments, "Owner") != "false", "{0} is held by the owner unless it says Owner = false", because);
            permission.EveryMember.Should().Be(Setting(on.Arguments, "EveryMember") == "true", "{0} is held by every member when it says EveryMember = true", because);
            permission.InRoles.Should().Be(Setting(on.Arguments, "InRoles") != "false", "{0} is given by a role unless it says InRoles = false", because);
            permission.FromAbove.Should().Be(Setting(on.Arguments, "FromAbove") != "false", "{0} counts from above unless it says FromAbove = false", because);

            var request = permission.Request;
            tutorial.Should().MatchRegex(
                @"\[RequiresPermission\(BoardPermissions\." + permission.Constant + @"\)\]\npublic sealed record " + request.Name + @"\(\[OnResource\] BoardId Board",
                "the tutorial's {0} requires {1} on the board", request.Name, because);
            tutorial.Should().Contain(ExplorerDocs.Mapped(request.Route), "the tutorial's endpoints send {0} on {1}", request.Name, request.Route);
        }

        // Reach from above is on in the tutorial's rules at their end, which the fourth way needs.
        tutorial.Should().Contain("reachFromAbove: true);");
    }

    [Fact]
    public void The_roles_give_what_the_tutorials_give_them()
    {
        var tutorial = ExplorerDocs.Page(Tutorial);
        var constants = Declared(tutorial).ToDictionary(each => each.Constant, each => each.Name);

        var starters = Starter().Matches(tutorial).ToList();
        starters.Should().NotBeEmpty("the tutorial declares its starter role");
        foreach (var starter in starters)
        {
            var role = Data.BoardRoles.Should().ContainSingle(each => each.Name == starter.Groups["name"].Value).Subject;
            starter.Groups["permissions"].Value.Split(',').Select(each => constants[each.Trim().Replace("BoardPermissions.", string.Empty, StringComparison.Ordinal)])
                .Should().Equal(role.Permissions, "the starter role {0} gives what the tutorial lists", role.Name);
        }

        var made = Spaces().Replace(ExplorerDocs.Page(Tutorial) + "\n" + ExplorerDocs.Page("tenancy/tutorial.md"), " ");
        foreach (var role in Data.OrganizationRoles)
        {
            if (role.Every)
            {
                role.Name.Should().Be("Administrator");
                made.Should().Contain("That pack holds every permission in the catalogue.", "Ada's Administrator role holds the board permissions too");
                continue;
            }

            var found = RoleMade().Matches(made).SingleOrDefault(match => match.Groups["name"].Value == role.Name);
            found.Should().NotBeNull("a tutorial makes the role {0} with POST /roles", role.Name);
            Quoted().Matches(found!.Groups["permissions"].Value).Select(match => match.Groups[1].Value)
                .Should().Equal(role.Permissions, "{0} contains what the tutorial gives it", role.Name);
        }
    }

    [Fact]
    public void The_units_boards_owners_members_and_grants_are_the_ones_the_pages_draw()
    {
        // The units, as the tutorial draws them, South Bay included.
        var tree = ExplorerDocs.DiagramOf(Tutorial, "What we build");
        foreach (var unit in Data.Units)
        {
            var parents = tree.Lines.Where(line => line.Kind == "-->" && line.To == tree.IdOf(unit.Name)).Select(line => tree.Nodes[line.From].Name);
            parents.Should().BeEquivalentTo(unit.Parent is null ? [] : [NameOf(unit.Parent)], "the tutorial draws {0} where the explorer has it", unit.Name);
        }

        foreach (var page in new[] { (Overview, ReachFromAbove), (Tutorial, "What we build") })
        {
            var diagram = ExplorerDocs.DiagramOf(page.Item1, page.Item2);

            foreach (var board in Data.Boards)
            {
                var id = diagram.IdOf(board.Name);
                var small = diagram.Nodes[id].Small;
                var hangs = diagram.Lines.Single(line => line.Kind == "-.-" && line.To == id).From;
                diagram.Nodes[hangs].Name.Should().Be(NameOf(board.Unit), "{0} draws {1} at its unit", page.Item1, board.Name);
                small.Should().Contain("owner: " + CallerOf(board.Owner).Name, "{0} draws {1}'s owner", page.Item1, board.Name);
                foreach (var member in board.Members)
                {
                    small.Should().Contain(CallerOf(member.Caller).Name + ": " + string.Join(", ", member.Roles), "{0} draws {1}'s members", page.Item1, board.Name);
                }

                // No member the diagram draws is missing: every "Name: role" line names one.
                Regex.Matches(small, @"(?<name>\w+): ").Select(match => match.Groups["name"].Value).Where(name => name != "owner")
                    .Should().BeEquivalentTo(board.Members.Select(member => CallerOf(member.Caller).Name), "{0} draws {1} with these members", page.Item1, board.Name);
            }

            // The roles in the organization, at the unit where each is granted.
            foreach (var caller in Data.Callers.Where(caller => caller.System is null))
            {
                foreach (var grant in caller.Grants)
                {
                    diagram.Nodes[diagram.IdOf(NameOf(grant.Unit))].Small.Should().Contain(caller.Name + ": ", "{0} draws {1}'s roles at {2}", page.Item1, caller.Name, NameOf(grant.Unit))
                        .And.Contain(grant.Role);
                }
            }

            var drawn = diagram.Nodes.Values.Where(node => node.Small.Contains(": ", StringComparison.Ordinal) && Data.Units.Any(unit => unit.Name == node.Name))
                .Select(node => node.Small.Split(':')[0]);
            drawn.Should().BeEquivalentTo(Data.Callers.Where(caller => caller.Grants.Count > 0).Select(caller => caller.Name), "{0} draws a role for everyone the explorer gives one", page.Item1);
        }
    }

    [Fact]
    public void Who_sees_and_who_changes_each_board_is_what_the_pages_tables_say()
    {
        var view = Data.Permissions.Single(permission => permission.Name == "boards.view");
        var edit = Data.Permissions.Single(permission => permission.Name == "boards.edit");

        var overview = ExplorerDocs.Table(Overview, ReachFromAbove);
        overview.Select(row => row[0]).Should().BeEquivalentTo(People(), "the overview says what each of the explorer's people sees");
        foreach (var row in overview)
        {
            Boards(CallerByName(row[0]), view, sees: true).Should().Contain(row[1], "the overview says what {0} sees", row[0]);
        }

        var tutorial = ExplorerDocs.Table(Tutorial, "What we build");
        tutorial.Select(row => row[0]).Should().BeEquivalentTo(People(), "the tutorial says what each of the explorer's people sees");
        foreach (var row in tutorial)
        {
            var caller = CallerByName(row[0]);
            Boards(caller, view, sees: true).Should().Contain(row[1], "the tutorial says what {0} sees", row[0]);
            Boards(caller, edit, sees: false).Should().Contain(written => row[2].StartsWith(written, StringComparison.Ordinal), "the tutorial says what {0} can change", row[0]);
        }
    }

    private static IEnumerable<string> People() => Data.Callers.Where(caller => caller.System is null).Select(caller => caller.Name);

    [Fact]
    public void The_refusals_are_Membership_s_under_the_boards_codes_with_the_status_the_host_gives_them()
    {
        foreach (var refusal in new[] { Data.Refusals.NotFound, Data.Refusals.NotPermitted })
        {
            refusal.Code.Should().StartWith("boards.", "a refusal carries the code of the board's kind");
            var rule = "membership." + refusal.Code["boards.".Length..];
            MembershipRefusals.Codes.Should().Contain(rule);
            refusal.Status.Should().Be(ExplorerDocs.StatusOf(MembershipRefusals.KindOf(rule)), "the host of the tutorial answers {0} so", refusal.Code);
        }

        Data.Refusals.NotFound.Code.Should().EndWith(MembershipRefusals.NotFound["membership.".Length..]);
        Data.Refusals.NotPermitted.Code.Should().EndWith(MembershipRefusals.NotPermitted["membership.".Length..]);
    }

    [Fact]
    public void Every_request_the_explorer_says_the_tutorial_sends_gets_the_tutorial_s_answer()
    {
        Data.Tried.Should().NotBeEmpty();
        foreach (var tried in Data.Tried)
        {
            var caller = CallerOf(tried.Caller);
            var permission = Data.Permissions.Single(each => each.Name == tried.Permission);
            var board = Data.Boards.Single(each => each.Id == tried.Board);
            var (item, _) = ExplorerDocs.TryIt(Tutorial, tried.Step, tried.Item);
            var because = tried.Step + ", item " + tried.Item;

            item.Should().StartWith(tried.Item + ". As " + caller.Name, "{0} is sent by {1}", because, caller.Name);
            item.Should().MatchRegex(ExplorerDocs.RouteIn(permission.Request.Route).ToString(), "{0} sends {1}", because, permission.Request.Name);
            item.Should().Contain("`" + tried.Status + "`", "{0} answers {1}", because, tried.Status);

            Answer(caller, board, permission).Should().Be(tried.Code, "the explorer gives {0} the answer the tutorial gives", because);
            if (tried.Code is not null)
            {
                item.Should().Contain("`" + tried.Code + "`");
            }
        }
    }

    /// <summary>
    /// What the access check answers a caller who sends the request for <paramref name="permission"/> on a board: null
    /// when one of the five ways holds it, not-found when the caller does not see the board, not-permitted otherwise.
    /// </summary>
    private static string? Answer(Caller caller, Board board, PermissionShown permission)
        => Holds(caller, board, permission) ? null : Sees(caller, board) ? Data.Refusals.NotPermitted.Code : Data.Refusals.NotFound.Code;

    private static bool Holds(Caller caller, Board board, PermissionShown permission)
    {
        var member = board.Members.SingleOrDefault(each => each.Caller == caller.Id);
        var owner = board.Owner == caller.Id;
        return caller.System is not null
               || (owner && permission.Owner)
               || ((owner || member is not null) && permission.EveryMember)
               || (member is not null && permission.InRoles && member.Roles.Any(role => Data.BoardRoles.Single(each => each.Name == role).Permissions.Contains(permission.Name)))
               || (permission.FromAbove && Above(caller, board, permission.Name));
    }

    /// <summary>Whether the caller sees the board: as its owner, as a member, from above with the permission every member holds, or as system work.</summary>
    private static bool Sees(Caller caller, Board board)
        => caller.System is not null
           || board.Owner == caller.Id
           || board.Members.Any(member => member.Caller == caller.Id)
           || Above(caller, board, Data.Permissions.Single(permission => permission.EveryMember).Name);

    private static bool Above(Caller caller, Board board, string permission)
        => caller.Grants.Any(grant => Data.OrganizationRoles.Single(role => role.Name == grant.Role) is var role
                                      && (role.Every || role.Permissions.Contains(permission))
                                      && ExplorerDocs.IsAtOrBelow(Data.Units.ToDictionary(unit => unit.Id, unit => unit.Parent), board.Unit, grant.Unit));

    /// <summary>
    /// The boards a caller sees, or holds a permission on, in the ways the pages' tables write them: by name, and as
    /// "both boards" when that is all two.
    /// </summary>
    private static List<string> Boards(Caller caller, PermissionShown permission, bool sees)
    {
        var names = Data.Boards.Where(board => sees ? Sees(caller, board) : Holds(caller, board, permission)).Select(board => board.Name).ToList();
        return names.Count == 2 && Data.Boards.Count == 2 ? [ExplorerDocs.Listed(names), "both boards"] : [ExplorerDocs.Listed(names)];
    }

    /// <summary>The permissions the tutorial's BoardPermissions declares: level, its arguments, the constant and the permission.</summary>
    private static List<(string Level, string Arguments, string Constant, string Name)> Declared(string tutorial)
        => [.. PermissionDeclared().Matches(tutorial)
            .Select(match => (match.Groups["level"].Value, match.Groups["arguments"].Value, match.Groups["constant"].Value, match.Groups["name"].Value))
            .DistinctBy(each => each.Item3)];

    /// <summary>The value of a setting of an attribute, <c>EveryMember = true</c> or <c>Description = "..."</c>, or null.</summary>
    private static string? Setting(string arguments, string name)
        => Regex.Match(arguments, @"\b" + name + @" = (?:""(?<value>[^""]*)""|(?<value>\w+))") is { Success: true } found ? found.Groups["value"].Value : null;

    private static Caller CallerOf(string id) => Data.Callers.Single(caller => caller.Id == id);

    private static Caller CallerByName(string name) => Data.Callers.Single(caller => caller.Name == name);

    private static string NameOf(string unit) => Data.Units.Single(each => each.Id == unit).Name;

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

    private sealed record World(
        List<Unit> Units,
        List<OrganizationRole> OrganizationRoles,
        List<PermissionShown> Permissions,
        List<BoardRole> BoardRoles,
        List<Caller> Callers,
        List<Board> Boards,
        RefusalsShown Refusals,
        List<Tried> Tried);

    private sealed record Unit(string Id, string Name, string? Parent);

    private sealed record OrganizationRole(string Name, bool Every, List<string> Permissions);

    private sealed record PermissionShown(string Name, string Constant, string Description, bool Owner, bool EveryMember, bool InRoles, bool FromAbove, RequestShown Request);

    private sealed record RequestShown(string Name, string Route);

    private sealed record BoardRole(string Name, List<string> Permissions);

    private sealed record Caller(string Id, string Name, string? System, string? PlacedAt, List<Grant> Grants);

    private sealed record Grant(string Role, string Unit);

    private sealed record Board(string Id, string Name, string Unit, string Owner, List<Member> Members);

    private sealed record Member(string Caller, List<string> Roles);

    private sealed record RefusalsShown(RefusalShown NotFound, RefusalShown NotPermitted);

    private sealed record RefusalShown(string Code, int Status);

    private sealed record Tried(string Caller, string Board, string Permission, string Step, int Item, int Status, string? Code);
}
