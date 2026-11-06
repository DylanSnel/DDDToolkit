using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DTS.DDD.Access;
using DTS.DDD.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.Analyzers.Tests.Docs;

/// <summary>
/// docs/how-a-request-is-checked.md, held to what it shows. The page follows six requests, four of them the Tenancy
/// sample's own, from the attribute to the row, and every layer of it is code a reader can find. So its requests
/// compile as the page writes them and the sample's four are the sample's as the sample writes them; what it says the
/// generator writes is what the generator writes for them; written as the property they require the same; the
/// policies it shows are lines of the sample's newest export; the requirement of a module's own it builds works as
/// the page says, asking who is calling before it reads; and the walk-through the site draws over the page shows no
/// code the page does not, and gives no answer its row of the page's tables does not give.
/// </summary>
public partial class HowARequestIsCheckedDocsExampleTests
{
    private const string Page = "how-a-request-is-checked.md";

    private const string Requests = "1. What you write";

    private const string OwnRequirement = "A requirement of your own";

    /// <summary>The sample's requests the page shows as the sample writes them.</summary>
    private static readonly string[] SampleRequests = ["OrganizationUnits", "AddOrganizationUnit", "RenameSeat", "AddCrewMember"];

    [Fact]
    public void Every_example_of_the_page_is_one_of_the_sections_its_tests_compile()
    {
        // A plain csharp block is an example a test compiles; one anywhere else on the page would be compiled by nobody.
        var compiled = DocsExamples.Of(Page, Requests).Concat(DocsExamples.Of(Page, OwnRequirement)).ToList();
        PlainExamples().Should().BeEquivalentTo(compiled, "every example on the page is in a section this test compiles");
    }

    [Fact]
    public void The_walk_through_shows_no_code_the_page_does_not()
    {
        using var walkthrough = JsonDocument.Parse(File.ReadAllText(WalkthroughPath()));
        var page = Read().Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);

        var code = walkthrough.RootElement.GetProperty("code").EnumerateObject().ToList();
        code.Should().NotBeEmpty();
        foreach (var snippet in code)
        {
            foreach (var line in snippet.Value.GetProperty("lines").EnumerateArray().Select(each => each.GetString()!.Trim()).Where(line => line.Length > 0))
            {
                page.Should().Contain(line, "the walk-through's '{0}' shows only what the page shows, so nothing is only inside the component", snippet.Name);
            }
        }

        // And every connection it shows is one of the page's, caller by caller.
        foreach (var caller in walkthrough.RootElement.GetProperty("callers").EnumerateArray())
        {
            page.Should().Contain(caller.GetProperty("connection").GetString()!.Trim());
        }
    }

    [Fact]
    public void Every_answer_the_walk_through_gives_is_the_one_its_row_of_the_pages_table_gives()
    {
        using var walkthrough = JsonDocument.Parse(File.ReadAllText(WalkthroughPath()));
        var names = walkthrough.RootElement.GetProperty("callers").EnumerateArray()
            .ToDictionary(caller => caller.GetProperty("id").GetString()!, caller => caller.GetProperty("name").GetString()!);

        foreach (var scenario in walkthrough.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            var request = scenario.GetProperty("request").GetString()!;
            var table = TableOf(request);
            var answered = new HashSet<string>(StringComparer.Ordinal);

            foreach (var outcome in scenario.GetProperty("outcomes").EnumerateArray())
            {
                var name = outcome.GetProperty("row").GetString()!;
                table.Should().ContainKey(name, "the walk-through's answer for {0} is a row of the page's table for it", request);
                var row = table[name];
                answered.Add(name);
                var because = request + ", " + name;

                // Who: every caller the answer is for is named in the row.
                foreach (var caller in outcome.GetProperty("when").GetProperty("caller").EnumerateArray())
                {
                    var called = names[caller.GetString()!];
                    called = called.StartsWith("A ", StringComparison.Ordinal) ? called[2..] : called;
                    row.Who.Should().ContainEquivalentOf(called, "the row {0} names every caller the walk-through gives its answer for", because);
                }

                // The door: let through, or the code and the status it refuses with.
                var door = outcome.GetProperty("door");
                var passed = door.GetProperty("pass").GetBoolean();
                if (passed)
                {
                    row.Door.Should().StartWith("let through", "the row {0} lets the caller through, as the walk-through does", because);
                }
                else
                {
                    row.Door.Should().Contain(door.GetProperty("code").GetString()!, "the row {0} refuses with the walk-through's code", because)
                        .And.Contain(door.GetProperty("status").GetInt32().ToString(CultureInfo.InvariantCulture));
                }

                // At the door: a statement, or nothing.
                var sent = outcome.GetProperty("statement").GetProperty("snippet").ValueKind != JsonValueKind.Null;
                row.AtTheDoor.StartsWith("nothing", StringComparison.Ordinal).Should().Be(!sent, "the row {0} says whether the door sends a statement, as the walk-through does", because);

                // The rows, and what a statement round the door meets.
                var rows = outcome.GetProperty("rows");
                if (rows.TryGetProperty("round", out var round) && round.GetBoolean())
                {
                    row.RoundTheDoor.Should().StartWith(rows.GetProperty("result").GetString()!, "the row {0} gives what Postgres answers round the door, as the walk-through does", because);
                }
                else
                {
                    row.RoundTheDoor.Should().StartWith(NoAnswer, "the walk-through gives the row {0} no answer round the door", because);
                }

                if (!passed)
                {
                    row.Rows.Should().StartWith("none", "the handler of the row {0} does not run", because);
                }
                else if (rows.TryGetProperty("code", out var code))
                {
                    row.Rows.Should().Contain(code.GetString()!, "the row {0} is refused past the door, as the walk-through says", because)
                        .And.Contain(rows.GetProperty("status").GetInt32().ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    row.Rows.Should().Contain(Status().Replace(rows.GetProperty("result").GetString()!, string.Empty), "the row {0} reads or writes what the walk-through says", because);
                }
            }

            answered.Should().BeEquivalentTo(table.Keys, "every row of the page's table for {0} is an answer the walk-through gives", request);
        }
    }

    /// <summary>What a row of the page's tables says round the door where the walk-through gives no answer there.</summary>
    private const string NoAnswer = "–";

    /// <summary>A row of a table of Every request, every caller: its five cells.</summary>
    private sealed record Row(string Who, string Door, string AtTheDoor, string Rows, string RoundTheDoor);

    /// <summary>The table of <paramref name="request"/> under Every request, every caller, by the first cell of each row.</summary>
    private static Dictionary<string, Row> TableOf(string request)
    {
        var lines = DocsExamples.Section(Page, request).Split('\n').Where(line => line.StartsWith('|')).ToList();
        lines.Should().HaveCountGreaterThan(2, "Every request, every caller has a table for {0}", request);

        var table = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(2))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            cells.Should().HaveCount(5, "a row of the table for {0} says who, the door, at the door, the rows and round the door: {1}", request, line);
            table.Add(cells[0], new Row(cells[0], cells[1], cells[2], cells[3], cells[4]));
        }

        return table;
    }

    /// <summary>Asks the boards' checks about <paramref name="request"/> in a scope of its own, and throws what they throw.</summary>
    private static void Require(ServiceProvider provider, EmittedAssembly emitted, object request)
    {
        using var scope = provider.CreateScope();
        var checks = scope.ServiceProvider.GetRequiredService(typeof(AccessChecks<>).MakeGenericType(emitted.Type("Examples.Tenancy.IBoardsRequest")));
        try
        {
            ((ValueTask)checks.GetType().GetMethod("RequireAsync")!.Invoke(checks, [request, CancellationToken.None])!).GetAwaiter().GetResult();
        }
        catch (TargetInvocationException invoked) when (invoked.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
        }
    }

    private static RefusalException Refusal(ServiceProvider provider, EmittedAssembly emitted, object request)
        => FluentActions.Invoking(() => Require(provider, emitted, request)).Should().Throw<RefusalException>().Which;

    private static AccessRequirement Required(object? request) => ((IRequireAccess)request!).RequiredAccess;

    private static object PlanOf(EmittedAssembly emitted, string plan) => Enum.Parse(emitted.Type("Examples.Tenancy.Plan"), plan);

    private static void Set(EmittedAssembly emitted, object target, string property, object value)
        => target.GetType().GetProperty(property)!.SetValue(target, value);

    /// <summary>Whether a line of a generated block is one to find in what was generated: not a brace, and not the page's own elision.</summary>
    private static bool Shown(string line) => line.Length > 0 && line is not "{" and not "}" and not "};" && !line.StartsWith("// ...", StringComparison.Ordinal);

    /// <summary>The lines of <paramref name="example"/> that declare <paramref name="name"/>: its attributes, its declaration and its body.</summary>
    private static IEnumerable<string> DeclarationLines(string example, string name)
    {
        var lines = example.Split('\n').Select(line => line.Trim()).ToList();
        var declaration = lines.FindIndex(line => DeclarationOf(name).IsMatch(line));
        var start = declaration;
        while (start > 0 && lines[start - 1].StartsWith('['))
        {
            start--;
        }

        var end = declaration;
        if (!lines[declaration].EndsWith(';'))
        {
            end = lines.FindIndex(declaration, line => line == "}");
        }

        return lines.Skip(start).Take(end - start + 1).Where(line => line.Length > 0);
    }

    private static Regex DeclarationOf(string name) => new(@"\brecord " + name + @"\b[ (:]", RegexOptions.Multiline);

    /// <summary>The plain csharp blocks of the page: its examples.</summary>
    private static List<string> PlainExamples()
        => [.. PlainCsharp().Matches(Read()).Select(match => match.Groups["code"].Value)];

    private static string Read() => File.ReadAllText(Path.Combine(Root(), "docs", Page)).ReplaceLineEndings("\n");

    private static string WalkthroughPath() => Path.Combine(Root(), "website", "src", "components", "RequestWalkthrough", "walkthrough.json");

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return directory.FullName;
            }

            // A repository that has this one as its submodule core/ runs these tests from there.
            if (File.Exists(Path.Combine(directory.FullName, "core", "DDDToolkit.slnx")))
            {
                return Path.Combine(directory.FullName, "core");
            }
        }

        throw new DirectoryNotFoundException($"No DDDToolkit.slnx above '{AppContext.BaseDirectory}'.");
    }

    [GeneratedRegex(@"^```csharp\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex PlainCsharp();

    [GeneratedRegex(@"^```sql title=""(?<file>[^,""]+)[^""]*""\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex TitledSql();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>The status a walk-through's result leads with, such as "200 · ", which the page's table leaves out.</summary>
    [GeneratedRegex(@"^\d{3} · ")]
    private static partial Regex Status();
}
