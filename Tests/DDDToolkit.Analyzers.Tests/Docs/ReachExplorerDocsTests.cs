using System.Reflection;
using System.Text.RegularExpressions;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The reach explorer the site draws on docs/tenancy/overview.md, held to the page, the Tenancy tutorial and the code.
/// Its data, website/src/components/ReachExplorer/reach.json, is the tutorial's world at its end: two tenants, their
/// units, Ada, Rhea and Tove with their grants, four of Tenancy's permissions with the tutorial's request for each, and
/// the refusals those requests get. So every name in it is the tutorial's, every permission and refusal code is one the
/// code declares, the tree and the grants are the ones the tutorial draws and lists, and every request the explorer
/// says the tutorial sends gets the answer there that the tutorial's Try it gives. A permission or a unit renamed on
/// the page or in the code fails here until the explorer says it the same way.
/// </summary>
public partial class ReachExplorerDocsTests
{
    private const string Overview = "tenancy/overview.md";

    private const string Tutorial = "tenancy/tutorial.md";

    private const string WhatWeBuild = "What we build";

    private static readonly World Data = ExplorerDocs.Data<World>("ReachExplorer", "reach.json");

    [Fact]
    public void The_overview_marks_the_explorer_s_place_around_its_static_diagram()
    {
        var section = DocsExamples.Section(Overview, "Reach: a grant covers the units below it");
        section.Should().Contain("<div data-interactive=\"ReachExplorer\">\n\n```mermaid", "the static diagram is what GitHub shows in the explorer's place");
    }

    [Fact]
    public void Every_name_the_explorer_shows_is_the_overview_s_or_the_tutorial_s()
    {
        var docs = ExplorerDocs.Page(Overview) + "\n" + ExplorerDocs.Page(Tutorial);

        foreach (var name in Data.Tenants.Select(tenant => tenant.Name)
                     .Concat(Data.Tenants.SelectMany(tenant => tenant.Units).Select(unit => unit.Name))
                     .Concat(Data.People.Select(person => person.Name))
                     .Concat(Data.Roles.Select(role => role.Name)))
        {
            docs.Should().Contain(name, "the explorer shows {0}, which the overview or the tutorial names", name);
        }

        foreach (var code in Data.Permissions.Select(permission => permission.Name)
                     .Concat([Data.Refusals.NotPermitted.Code, Data.Refusals.NotSeated.Code])
                     .Concat(Data.Tenants.Select(tenant => "\"slug\": \"" + tenant.Slug + "\"")))
        {
            docs.Should().Contain(code, "the explorer shows {0}, which the overview or the tutorial writes", code);
        }

        // The box that ends a grant is off for a tenant's only administrator, for the reason the tutorial gives.
        ExplorerDocs.Page(Tutorial).Should().Contain("the last administrator cannot be removed");
    }

    [Fact]
    public void The_units_are_the_tree_the_tutorial_draws_in_each_tenant()
    {
        var diagram = ExplorerDocs.DiagramOf(Tutorial, WhatWeBuild);

        foreach (var tenant in Data.Tenants)
        {
            var drawn = diagram.Nodes.Where(node => node.Value.Subgraph == "Tenant: " + tenant.Name).ToDictionary(node => node.Key, node => node.Value.Name);
            drawn.Values.Should().BeEquivalentTo(tenant.Units.Select(unit => unit.Name), "the tutorial draws the units of {0}", tenant.Name);

            foreach (var unit in tenant.Units)
            {
                var id = diagram.IdOf(unit.Name);
                var parents = diagram.Lines.Where(line => line.Kind == "-->" && line.To == id).Select(line => diagram.Nodes[line.From].Name).ToList();
                var parent = unit.Parent is null ? null : tenant.Units.Single(each => each.Id == unit.Parent).Name;
                parents.Should().BeEquivalentTo(parent is null ? [] : [parent], "the tutorial draws {0} below {1}", unit.Name, parent ?? "nothing");
            }
        }
    }

    [Fact]
    public void The_people_their_seats_and_their_grants_are_the_rows_of_the_tutorial_s_table()
    {
        var rows = ExplorerDocs.Table(Tutorial, WhatWeBuild);
        rows.Select(row => row[0]).Should().BeEquivalentTo(Data.People.Select(person => person.Name), "the tutorial lists the explorer's people");

        foreach (var person in Data.People)
        {
            var row = rows.Single(each => each[0] == person.Name);
            var tenant = Data.Tenants.Single(each => each.Id == person.Tenant);
            var grant = person.Grants.Should().ContainSingle("the tutorial gives {0} one role", person.Name).Subject;

            row[1].Should().Be(tenant.Name, "{0}'s seat is in the tenant the tutorial says", person.Name);
            row[2].Should().Be(NameOf(person.PlacedAt), "{0} is placed where the tutorial says", person.Name);
            row[3].Should().Be(grant.Role + ", at " + (Parents()[grant.Unit] is null ? "the root" : NameOf(grant.Unit)), "{0}'s role and where it is granted", person.Name);

            // Where the grant counts: the unit of the grant and every unit below it.
            var reached = tenant.Units.Where(unit => ExplorerDocs.IsAtOrBelow(Parents(), unit.Id, grant.Unit)).Select(unit => unit.Name).ToList();
            row[4].Should().Be(reached.Count == tenant.Units.Count ? "every unit of " + tenant.Name : ExplorerDocs.Listed(reached), "{0}'s grant reaches where the tutorial says", person.Name);

            // The only seat holding the administrators' role at the root of its tenant: the explorer cannot end that grant.
            var administrators = Data.People.Count(other => other.Tenant == person.Tenant && other.Grants.Any(each => each.Role == "Administrator" && Parents()[each.Unit] is null));
            person.OnlyAdministrator.Should().Be(grant.Role == "Administrator" && administrators == 1, "{0} is or is not the only administrator of {1}", person.Name, tenant.Name);
        }
    }

    [Fact]
    public void The_roles_contain_the_permissions_the_tutorial_gives_them()
    {
        var tutorial = Spaces().Replace(ExplorerDocs.Page(Tutorial), " ");
        foreach (var role in Data.Roles)
        {
            if (role.Every)
            {
                role.Name.Should().Be("Administrator", "only the administrators' role contains every permission");
                ExplorerDocs.Page(Overview).Should().Contain("One role, Administrator, with every permission.");
                continue;
            }

            var made = RoleMade().Matches(tutorial).SingleOrDefault(match => match.Groups["name"].Value == role.Name);
            made.Should().NotBeNull("the tutorial makes the role {0} with POST /roles", role.Name);
            Permissions(made!.Groups["permissions"].Value).Should().Equal(role.Permissions, "{0} contains what the tutorial gives it", role.Name);
        }
    }

    [Fact]
    public void Each_permission_is_Tenancy_s_with_its_description_and_its_request_is_the_tutorial_s()
    {
        var tutorial = ExplorerDocs.Page(Tutorial);
        foreach (var permission in Data.Permissions)
        {
            var declared = TenancyPermissions.All.SingleOrDefault(each => each.Name == permission.Name);
            declared.Should().NotBeNull("{0} is one of Tenancy's permissions", permission.Name);
            permission.Description.Should().Be(declared!.Description, "the explorer shows the catalogue's description of {0}", permission.Name);

            var constant = typeof(TenancyPermissions).GetFields(BindingFlags.Public | BindingFlags.Static)
                .First(field => field.IsLiteral && (string?)field.GetRawConstantValue() == permission.Name).Name;
            var request = permission.Request;
            var declaration = new Regex(@"\[RequiresPermission\(TenancyPermissions\." + constant + @"\)\]\npublic sealed record " + request.Name + @"\((?<parameters>[^)]*)\)")
                .Match(tutorial);
            declaration.Success.Should().BeTrue("the tutorial's {0} requires {1}", request.Name, permission.Name);

            // Where the permission is checked: at a unit with [AtUnit], for the whole tenant without it.
            request.At.Should().Be(declaration.Groups["parameters"].Value.Contains("[AtUnit]", StringComparison.Ordinal) ? "unit" : "tenant", "{0} checks {1} where its declaration says", request.Name, permission.Name);
            if (request.At == "unit")
            {
                request.Where.Should().EndWith("or above it", "a permission held above a unit counts at the unit");
            }
            else
            {
                request.Where.Should().Be("for the whole tenant");
            }

            tutorial.Should().Contain(ExplorerDocs.Mapped(request.Route), "the tutorial's endpoints send {0} on {1}", request.Name, request.Route);
        }
    }

    [Fact]
    public void The_refusals_are_Tenancy_s_with_the_status_the_tutorial_s_host_gives_them()
    {
        foreach (var refusal in new[] { Data.Refusals.NotPermitted, Data.Refusals.NotSeated })
        {
            TenancyRefusals.Codes.Should().Contain(refusal.Code);
            refusal.Status.Should().Be(ExplorerDocs.StatusOf(TenancyRefusals.KindOf(refusal.Code)), "the host of the tutorial answers {0} so", refusal.Code);
        }
    }

    [Fact]
    public void Every_request_the_explorer_says_the_tutorial_sends_gets_the_tutorial_s_answer()
    {
        Data.Tried.Should().NotBeEmpty();
        foreach (var tried in Data.Tried)
        {
            var person = Data.People.Single(each => each.Id == tried.Person);
            var permission = Data.Permissions.Single(each => each.Name == tried.Permission);
            var (item, section) = ExplorerDocs.TryIt(Tutorial, tried.Step, tried.Item);
            var because = tried.Step + ", item " + tried.Item;

            section.Should().Contain("As " + person.Name, "{0} is sent by {1}", because, person.Name);
            item.Should().MatchRegex(ExplorerDocs.RouteIn(permission.Request.Route).ToString(), "{0} sends {1}", because, permission.Request.Name);
            if (permission.Request.Phrase is not null)
            {
                var unit = Parents()[tried.Unit] is null ? "root" : NameOf(tried.Unit);
                item.Should().Contain(unit, "{0} names the unit the explorer sends it for", because);
            }

            if (tried.Status is { } status)
            {
                item.Should().Contain("`" + status + "`", "{0} answers {1}", because, status);
            }

            var answer = Answer(person, permission, tried.Unit);
            answer.Should().Be(tried.Code, "the explorer gives {0} the answer the tutorial gives", because);
            if (tried.Code is not null)
            {
                item.Should().Contain("`" + tried.Code + "`");
            }
            else
            {
                item.Should().NotMatchRegex(@"`40\d`", "{0} is let through", because);
            }
        }
    }

    /// <summary>What the access check answers the tutorial's request for a permission at a unit: null when it lets the request through.</summary>
    private static string? Answer(Person person, PermissionShown permission, string unitId)
    {
        var tenant = Data.Tenants.Single(each => each.Units.Any(unit => unit.Id == unitId));
        if (tenant.Id != person.Tenant)
        {
            return Data.Refusals.NotSeated.Code;
        }

        var at = permission.Request.At == "tenant" ? tenant.Units.Single(unit => unit.Parent is null).Id : unitId;
        var holds = person.Grants.Any(grant => Contains(grant.Role, permission.Name) && ExplorerDocs.IsAtOrBelow(Parents(), at, grant.Unit));
        return holds ? null : Data.Refusals.NotPermitted.Code;
    }

    private static bool Contains(string role, string permission)
        => Data.Roles.Single(each => each.Name == role) is var found && (found.Every || found.Permissions.Contains(permission));

    private static Dictionary<string, string?> Parents()
        => Data.Tenants.SelectMany(tenant => tenant.Units).ToDictionary(unit => unit.Id, unit => unit.Parent);

    private static string NameOf(string unit) => Data.Tenants.SelectMany(tenant => tenant.Units).Single(each => each.Id == unit).Name;

    private static List<string> Permissions(string list) => [.. Quoted().Matches(list).Select(match => match.Groups[1].Value)];

    [GeneratedRegex(@"\{ ""name"": ""(?<name>[^""]+)"", ""description"": ""[^""]*"", ""permissions"": \[(?<permissions>[^\]]*)\] \}")]
    private static partial Regex RoleMade();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private sealed record World(List<Tenant> Tenants, List<Role> Roles, List<Person> People, List<PermissionShown> Permissions, RefusalsShown Refusals, List<Tried> Tried);

    private sealed record Tenant(string Id, string Name, string Slug, List<Unit> Units);

    private sealed record Unit(string Id, string Name, string? Parent);

    private sealed record Role(string Name, bool Every, List<string> Permissions);

    private sealed record Person(string Id, string Name, string Tenant, string PlacedAt, List<Grant> Grants, bool OnlyAdministrator);

    private sealed record Grant(string Role, string Unit);

    private sealed record PermissionShown(string Name, string Description, RequestShown Request);

    private sealed record RequestShown(string Name, string Route, string At, string? Phrase, string Where);

    private sealed record RefusalsShown(RefusalShown NotPermitted, RefusalShown NotSeated);

    private sealed record RefusalShown(string Code, int Status);

    private sealed record Tried(string Person, string Permission, string Unit, string Step, int Item, int? Status, string? Code);
}
