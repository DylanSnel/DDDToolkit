using System.Reflection;
using System.Text.RegularExpressions;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The "Who acts" chooser the site draws on docs/tenancy/keeping-tenants-apart.md, held to the page and the code. Its
/// data, website/src/components/WhoActs/whoActs.json, is the eight situations of the page's table under "Which call for
/// which situation": the title, the call or attribute to use, what the work holds, how it is recorded and the database
/// role are that table's cells, in its order, so the chooser never says what the page does not. Every call it names is
/// a member of Tenancy's access manager, every attribute is Tenancy's, every recorded kind is one Tenancy writes, every
/// refusal code is one the code declares, every database role is one the docs name, and every link goes to a heading
/// that exists. A call renamed in the code, or a cell changed on the page, fails here until the chooser says it too.
/// </summary>
public partial class WhoActsDocsTests
{
    private const string Page = "tenancy/keeping-tenants-apart.md";

    private const string Heading = "Which call for which situation";

    private static readonly Chooser Data = ExplorerDocs.Data<Chooser>("WhoActs", "whoActs.json");

    [Fact]
    public void The_page_marks_the_chooser_s_place_around_its_table()
    {
        var section = DocsExamples.Section(Page, Heading);
        section.Should().Contain("<div data-interactive=\"WhoActs\">\n\n| Situation |", "the table is what GitHub shows in the chooser's place");
        section.Should().Contain("|\n\n</div>", "the marker closes after the table");
    }

    [Fact]
    public void The_situations_are_the_rows_of_the_page_s_table_in_its_order()
    {
        var rows = ExplorerDocs.Table(Page, Heading);
        rows.Select(row => row[0]).Should().Equal(Data.Situations.Select(situation => situation.Title), "the chooser shows the page's situations in the page's order");

        foreach (var situation in Data.Situations)
        {
            var row = rows.Single(each => each[0] == situation.Title);
            row.Should().HaveCount(5, "a row is the situation, the call, what it holds, how it is recorded and the database role");
            row[1].Should().Be(situation.Use, "the chooser names the call the page names for {0}", situation.Title);
            row[2].Should().Be(situation.Holds, "the chooser says what the work holds as the page does for {0}", situation.Title);
            row[3].Should().Be(situation.RecordedAs, "the chooser says how {0} is recorded as the page does", situation.Title);
            row[4].Should().Be(situation.Postgres, "the chooser names the database role the page names for {0}", situation.Title);
            row[3].Should().StartWith("`" + situation.Kind + "`", "{0} is recorded as the kind the chooser colours it with", situation.Title);
        }
    }

    [Fact]
    public void Every_situation_is_filled_in()
    {
        Data.Situations.Select(situation => situation.Id).Should().OnlyHaveUniqueItems();
        Data.Situations.Select(situation => situation.Label).Should().OnlyHaveUniqueItems();

        foreach (var situation in Data.Situations)
        {
            new[] { situation.Label, situation.Story, situation.ActsIn, situation.RecordedDetail, situation.PostgresDetail }
                .Should().NotContain(text => string.IsNullOrWhiteSpace(text), "every part of {0} is said", situation.Title);
            situation.Why.Should().NotBeEmpty("{0} says why it fits", situation.Title);
            situation.Notes.Should().NotBeEmpty("{0} says what to watch out for", situation.Title);
            situation.Code.Should().NotBeEmpty("{0} shows a handler", situation.Title);
        }
    }

    [Fact]
    public void Every_database_role_is_one_the_docs_name()
    {
        string[] roles = ["ddd_system_in", "authenticated"];
        var docs = ExplorerDocs.Page("tenancy/postgres.md") + "\n" + ExplorerDocs.Page("row-level-security.md");

        foreach (var situation in Data.Situations)
        {
            situation.Roles.Should().NotBeEmpty("{0} names the role it runs as", situation.Title);
            foreach (var role in situation.Roles)
            {
                roles.Should().Contain(role, "{0} runs as a role the toolkit uses", situation.Title);
                docs.Should().Contain("`" + role + "`", "the docs name {0}", role);
                situation.Postgres.Should().Contain("`" + role + "`", "{0} says it runs as {1}", situation.Title, role);
            }
        }
    }

    [Fact]
    public void Every_link_goes_to_a_heading_that_exists()
    {
        foreach (var link in Data.Situations.SelectMany(situation => situation.Links))
        {
            var parts = link.To.Split('#', 2);
            parts[0].Should().StartWith("/docs/", "a link goes to a docs page: {0}", link.To);
            var page = parts[0]["/docs/".Length..] + ".md";
            File.Exists(Path.Combine(ExplorerDocs.Root(), "docs", page)).Should().BeTrue("{0} goes to a page of docs/", link.To);

            parts.Should().HaveCount(2, "{0} goes to a heading", link.To);
            Anchors(page).Should().Contain(parts[1], "{0} goes to a heading of docs/{1}", link.To, page);
        }
    }

    /// <summary>The anchors the site makes for the headings of a page: lower case, punctuation dropped, spaces as dashes.</summary>
    private static HashSet<string> Anchors(string page)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var fenced = false;
        foreach (var line in ExplorerDocs.Page(page).Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced && HeadingLine().Match(line) is { Success: true } heading)
            {
                anchors.Add(NotInAnchor().Replace(heading.Groups["text"].Value.ToLowerInvariant(), "").Replace(' ', '-'));
            }
        }

        return anchors;
    }

    [GeneratedRegex(@"^#{1,6} (?<text>.+)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"[^\p{L}\p{M}\p{N}\p{Pc} -]")]
    private static partial Regex NotInAnchor();

    [GeneratedRegex(@"`(?<code>(?:tenancy|access)\.[a-z-]+)`")]
    private static partial Regex RefusalCode();

    private sealed record Chooser(List<Situation> Situations);

    private sealed record Situation(
        string Id,
        string Label,
        string Title,
        string Use,
        string Story,
        List<string> Why,
        string ActsIn,
        string Holds,
        string RecordedAs,
        string RecordedDetail,
        string Postgres,
        string PostgresDetail,
        string Kind,
        List<string> Notes,
        List<string> Calls,
        List<string> Attributes,
        string? OperatorTenant,
        List<string> Refusals,
        List<string> Roles,
        List<string> Code,
        List<LinkShown> Links);

    private sealed record LinkShown(string Text, string To);
}
