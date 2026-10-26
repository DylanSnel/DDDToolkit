using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Docs;

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

    /// <summary>What the page leaves out of every example: the usings, the module interfaces' namespaces among them.</summary>
    private const string Header =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.Exceptions;
        using DDDToolkit.Supporting.Membership.Access;
        using DDDToolkit.Supporting.Tenancy.Access;
        using DDDToolkit.Supporting.Tenancy.Catalogue;
        using Examples.Tenancy.Projects.Application.Access;
        using Examples.Tenancy.Tenants.Application.Access;
        using Mediator;
        using Microsoft.Extensions.DependencyInjection;

        """;

    /// <summary>
    /// What the sample declares elsewhere and the page's requests name: the ids, a listing and a kind of unit, the
    /// projects' keys, and the module interfaces in the sample's namespaces. And what a test adds to run the
    /// requirement of the module's own: a plan it answers, stand-ins for Tenancy's answers about the caller and for
    /// its check of the key, and counts of how often each is asked.
    /// </summary>
    private const string TestOnly =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.Exceptions;
        using DDDToolkit.Supporting.Tenancy.Access;

        namespace Examples.Tenancy
        {
            [EntityId<Guid>] public readonly partial record struct TenantId;
            [EntityId<Guid>] public readonly partial record struct SeatId;
            [EntityId<Guid>] public readonly partial record struct OrganizationUnitId;
            [EntityId<Guid>] public readonly partial record struct RoleId;
            [EntityId<Guid>] public readonly partial record struct ProjectId;
            [EntityId<Guid>] public readonly partial record struct ProjectRoleId;
            [EntityId<Guid>] public readonly partial record struct BoardId;

            public enum UnitKind { Region, Area }

            public sealed record UnitListing(OrganizationUnitId Id, string Name);

            public sealed record AuditEntry(string What);

            public readonly record struct PagingArguments(int? First, string? After);

            public sealed record Page<T>(IReadOnlyList<T> Items);

            public static class ProjectKeys
            {
                public const string ManageCrew = "projects.crew.manage";
            }

            public static class BoardKeys
            {
                public const string Export = "boards.export";
            }

            [AccessRequests] public partial interface IAuditRequest : IRequireAccess;

            public sealed class PlanOfTheTest : ITenantPlans
            {
                public Plan Plan { get; set; }

                public int Asked { get; set; }

                public TenantId? AskedFor { get; set; }

                public ValueTask<Plan> CurrentAsync(TenantId tenant, CancellationToken cancellationToken)
                {
                    Asked++;
                    AskedFor = tenant;
                    return ValueTask.FromResult(Plan);
                }
            }

            /// <summary>Tenancy's answers about the caller: a seat of the tenant, or nobody there, refused as Tenancy refuses one.</summary>
            public sealed class AnswersOfTheTest : ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>
            {
                public bool Seated { get; set; } = true;

                public TenantId Tenant { get; } = TenantId.Create();

                public TenancyCaller<TenantId, SeatId> Caller => throw new NotSupportedException("The page's check asks RequireTenant alone.");

                public TenantInScope<TenantId, SeatId> RequireTenant()
                    => Seated
                        ? new TenantInScope<TenantId, SeatId>(Tenant, SeatId.Create(), BySystem: false)
                        : throw DDDToolkit.Supporting.Tenancy.TenancyRefusals.Refuse(DDDToolkit.Supporting.Tenancy.TenancyRefusals.NotSeated);

                public ITenancyQuestions<TenantId, SeatId, OrganizationUnitId, RoleId> Over(
                    ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> source,
                    IQueryExecutor executor)
                    => throw new NotSupportedException("The page's check asks RequireTenant alone.");
            }

            public sealed class KeyOfTheTest
            {
                public bool Held { get; set; }

                public int Asked { get; set; }
            }

            public sealed class KeyCheckOfTheTest(KeyOfTheTest key) : IAccessCheck
            {
                public bool Decides(AccessRequirement requirement) => requirement is TenancyRequirement;

                public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
                {
                    key.Asked++;
                    return key.Held
                        ? ValueTask.CompletedTask
                        : ValueTask.FromException(new RefusalException("tenancy.not-permitted", RefusalKind.NotPermitted, "You lack the permission."));
                }
            }
        }

        namespace Examples.Tenancy.Tenants.Application.Access
        {
            [AccessRequests] public partial interface ITenantsRequest : IRequireAccess;
        }

        namespace Examples.Tenancy.Projects.Application.Access
        {
            // As the sample's: the module's default is a caller in the tenant.
            [AccessRequests, InTenant] public partial interface IProjectsRequest : IRequireAccess;
        }
        """;

    /// <summary>The namespaces a generated name is written in full with, longest first, which the page leaves out.</summary>
    private static readonly string[] WrittenInFull =
    [
        "Examples.Tenancy.Tenants.Application.Access.",
        "Examples.Tenancy.Projects.Application.Access.",
        "Examples.Tenancy.",
        "DDDToolkit.Supporting.Membership.Access.",
        "DDDToolkit.Supporting.Tenancy.Access.",
        "DDDToolkit.Access.",
        "Microsoft.Extensions.DependencyInjection.",
        "System.Threading.Tasks.",
        "System.Threading.",
        "System.",
        "Mediator.",
    ];

    [Fact]
    public void Every_example_of_the_page_is_one_of_the_sections_its_tests_compile()
    {
        // A plain csharp block is an example a test compiles; one anywhere else on the page would be compiled by nobody.
        var compiled = DocsExamples.Of(Page, Requests).Concat(DocsExamples.Of(Page, OwnRequirement)).ToList();
        PlainExamples().Should().BeEquivalentTo(compiled, "every example on the page is in a section this test compiles");
    }

    [Fact]
    public void The_examples_compile_as_the_page_writes_them_and_nothing_is_reported()
    {
        var result = Run();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("the requests say what they require one way each, and the module's own attribute is declared as a package declares one");
    }

    [Fact]
    public void The_sample_writes_its_four_requests_as_the_page_does()
    {
        var examples = DocsExamples.Of(Page, Requests).Take(2).ToList();
        foreach (var name in SampleRequests)
        {
            var example = examples.Single(each => DeclarationOf(name).IsMatch(each));
            var file = SampleFileOf(name);
            var lines = File.ReadAllLines(file).Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);

            foreach (var line in DeclarationLines(example, name))
            {
                lines.Should().Contain(line, "the page shows {0} as {1} writes it", name, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_module_interfaces_the_test_stands_in_carry_the_sample_s_defaults()
    {
        // The page says which last arm each module's switch has: Tenants' stops a request it has no line for, and
        // Projects' answers the module's default. So the stand-ins carry the sample's default, or none, as the sample does.
        foreach (var (name, declared) in new[] { ("ITenantsRequest", "[AccessRequests]"), ("IProjectsRequest", "[AccessRequests, InTenant]") })
        {
            File.ReadAllLines(SampleFileOf(name)).Select(line => line.Trim()).Should().Contain(declared, "the sample declares {0} so", name);
            TestOnly.Should().Contain(declared + " public partial interface " + name + " : IRequireAccess;");
        }

        Run().Source("ProjectsAccessBehavior.").Should().Contain("_ => global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.InTenant(),", "the page says Projects' last arm answers the default");
        DocsExamples.Titled(Page, "ProjectsAccessBehavior.g.cs for this page's requests, the interface's part, shortened").Should().Contain("a last arm that answers the module's default, TenancyAccess.InTenant()");
    }

    [Theory]
    [InlineData("TenantsAccessBehavior.g.cs for this page's requests, the interface's part, shortened", "TenantsAccessBehavior.")]
    [InlineData("ProjectsAccessBehavior.g.cs for this page's requests, the interface's part, shortened", "ProjectsAccessBehavior.")]
    [InlineData("TenantsAccessBehavior.g.cs, the behavior, shortened", "TenantsAccessBehavior.")]
    [InlineData("BoardsAccessBehavior.g.cs, the interface's part, shortened", "BoardsAccessBehavior.")]
    public void What_the_page_says_the_generator_writes_is_what_it_writes(string title, string hint)
    {
        var written = Written(Run().Source(hint));

        foreach (var line in Written(DocsExamples.Titled(Page, title)).Split('\n').Where(Shown))
        {
            written.Should().Contain(line, "the page shows '{0}' as the generator writes it", title);
        }
    }

    [Fact]
    public void Written_as_the_property_each_request_requires_what_its_attributes_do()
    {
        var emitted = Run().Emit();
        var unit = emitted.New("Examples.Tenancy.OrganizationUnitId", Guid.NewGuid());
        var seat = emitted.New("Examples.Tenancy.SeatId", Guid.NewGuid());
        var project = emitted.New("Examples.Tenancy.ProjectId", Guid.NewGuid());

        object?[][] made =
        [
            ["OrganizationUnits"],
            ["AddOrganizationUnit", unit, "Kade 3", null],
            ["RenameSeat", seat, "Ada L."],
            ["AddCrewMember", project, seat, null, null, 7L],
            ["RegisterOrganization", "harbor-two", "Harbor Two", "Ada"],
        ];

        foreach (var arguments in made)
        {
            var name = (string)arguments[0]!;
            var attribute = Required(emitted.New("Examples.Tenancy." + name, arguments[1..]));
            var property = Required(emitted.New("Examples.Tenancy.AsProperty." + name, arguments[1..]));

            property.Should().Be(attribute, "{0} written as the property requires what its attributes say", name);
        }

        // The audit log says it as the property alone, and C# takes its own over the interface's default.
        Required(emitted.New("Examples.Tenancy.TenantAuditLog", emitted.New("Examples.Tenancy.TenantId", Guid.NewGuid()), emitted.New("Examples.Tenancy.PagingArguments", null, null)))
            .Should().Be(AccessRequirement.Any(TenancyAccess.RequiresOperator(), TenancyAccess.AtTenant("tenancy.seats.manage")));
    }

    [Fact]
    public void The_attributes_and_the_markers_are_the_calls_the_page_names()
    {
        var emitted = Run().Emit();
        var unit = emitted.New("Examples.Tenancy.OrganizationUnitId", Guid.NewGuid());
        var seat = emitted.New("Examples.Tenancy.SeatId", Guid.NewGuid());
        var project = emitted.New("Examples.Tenancy.ProjectId", Guid.NewGuid());

        Required(emitted.New("Examples.Tenancy.OrganizationUnits")).Should().Be(TenancyAccess.InTenant());
        Required(emitted.New("Examples.Tenancy.RegisterOrganization", "harbor-two", "Harbor Two", "Ada")).Should().Be(AccessRequirement.SignedIn());
        Required(emitted.New("Examples.Tenancy.AddCrewMember", project, seat, null, null, 7L)).ToString()
            .Should().Contain("projects.crew.manage").And.Contain("7", "the version the caller read, which the check compares once the caller may act");
        Required(emitted.New("Examples.Tenancy.AddOrganizationUnit", unit, "Kade 3", null)).GetType().Name.Should().Be("AtUnit`1");
        Required(emitted.New("Examples.Tenancy.RenameSeat", seat, "Ada L."))
            .Should().BeOfType<AccessRequirement.AnyOf>("yours, or the key: [OrOwn] beside [RequiresKey]");
    }

    [Fact]
    public void A_requirement_of_the_modules_own_is_asked_first_and_the_key_after_it_as_the_attributes_are_written()
    {
        var emitted = Run().Emit();
        var plan = emitted.New("Examples.Tenancy.PlanOfTheTest")!;
        var key = emitted.New("Examples.Tenancy.KeyOfTheTest")!;
        var answers = emitted.New("Examples.Tenancy.AnswersOfTheTest")!;
        using var provider = Services(emitted, plan, key, answers, withPlanCheck: true);
        var export = emitted.New("Examples.Tenancy.ExportBoard", emitted.New("Examples.Tenancy.BoardId", Guid.NewGuid()))!;

        Required(export).Should().Be(
            AccessRequirement.All((AccessRequirement)emitted.CallStatic("Examples.Tenancy.PlanAtLeast", "RequiresPlan", PlanOf(emitted, "Pro"))!, TenancyAccess.AtTenant("boards.export")),
            "stacked attributes are All of their calls, in the order they are written");

        // A tenant on the Free plan is refused with the module's code, and Tenancy is asked nothing.
        Set(emitted, plan, "Plan", PlanOf(emitted, "Free"));
        Set(emitted, key, "Held", true);
        Refusal(provider, emitted, export).Code.Should().Be("boards.plan-too-low");
        emitted.Property(key, "Asked").Should().Be(0, "the first case refused, so the second is not asked");
        emitted.Property(plan, "AskedFor").Should().Be(emitted.Property(answers, "Tenant"), "the plan read is the one of the tenant the caller works in");

        // On the Pro plan the key is asked next, and its refusal is its own.
        Set(emitted, plan, "Plan", PlanOf(emitted, "Pro"));
        Set(emitted, key, "Held", false);
        Refusal(provider, emitted, export).Code.Should().Be("tenancy.not-permitted");

        Set(emitted, key, "Held", true);
        FluentActions.Invoking(() => Require(provider, emitted, export)).Should().NotThrow();
        emitted.Property(key, "Asked").Should().Be(2);
    }

    [Fact]
    public void A_caller_who_is_nobody_in_the_tenant_is_refused_with_its_own_reason_before_any_plan_is_read()
    {
        var emitted = Run().Emit();
        var plan = emitted.New("Examples.Tenancy.PlanOfTheTest")!;
        var key = emitted.New("Examples.Tenancy.KeyOfTheTest")!;
        var answers = emitted.New("Examples.Tenancy.AnswersOfTheTest")!;
        using var provider = Services(emitted, plan, key, answers, withPlanCheck: true);
        var export = emitted.New("Examples.Tenancy.ExportBoard", emitted.New("Examples.Tenancy.BoardId", Guid.NewGuid()))!;

        // The new sign-in, an operator or an anonymous caller: nobody in the tenant, so no tenant's plan to learn.
        Set(emitted, answers, "Seated", false);
        Set(emitted, plan, "Plan", PlanOf(emitted, "Free"));
        Refusal(provider, emitted, export).Code.Should().Be("tenancy.not-seated", "the caller's own reason comes before anything is read");
        emitted.Property(plan, "Asked").Should().Be(0, "a caller who is nobody in the tenant learns nothing of its plan");
        emitted.Property(key, "Asked").Should().Be(0, "the first case refused, so the second is not asked");
    }

    [Fact]
    public void A_module_that_forgets_its_check_has_the_request_stopped_with_the_registration_named()
    {
        var emitted = Run().Emit();
        using var provider = Services(emitted, emitted.New("Examples.Tenancy.PlanOfTheTest")!, emitted.New("Examples.Tenancy.KeyOfTheTest")!, emitted.New("Examples.Tenancy.AnswersOfTheTest")!, withPlanCheck: false);
        var export = emitted.New("Examples.Tenancy.ExportBoard", emitted.New("Examples.Tenancy.BoardId", Guid.NewGuid()))!;

        FluentActions.Invoking(() => Require(provider, emitted, export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*AddAccessCheck<IBoardsRequest, TCheck>()*", "a case nothing decides lets nobody through, and says which registration is missing");
    }

    [Fact]
    public void The_policies_the_page_shows_are_lines_of_the_samples_newest_export()
    {
        var blocks = TitledSql().Matches(Read()).ToList();
        blocks.Should().NotBeEmpty();

        foreach (var block in blocks)
        {
            var file = block.Groups["file"].Value;
            var path = Path.Combine(Migrations(), file);
            File.Exists(path).Should().BeTrue("the page names {0}, which the sample's export wrote", file);

            var module = Regex.Match(file, @"_access\.(?<module>\w+)\.ddd\.sql$").Groups["module"].Value;
            var newest = Directory.GetFiles(Migrations(), "*_access." + module + ".ddd.sql").Select(Path.GetFileName).Order(StringComparer.Ordinal).Last();
            file.Should().Be(newest, "the page shows what the policies of {0} are now, which the newest access file says", module);

            var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd()).ToHashSet(StringComparer.Ordinal);
            foreach (var line in block.Groups["code"].Value.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0))
            {
                lines.Should().Contain(line, "the page copies {0} line by line", file);
            }
        }
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

    /// <summary>
    /// The page's examples in one project of the application, as the sample's modules see Tenancy, Membership and
    /// Mediator: the requests of the first section in the application's namespace, the same requests written as the
    /// property in one of their own, and the module of the last section.
    /// </summary>
    private static GeneratorRunOutcome Run()
    {
        var requests = DocsExamples.Of(Page, Requests);
        requests.Should().HaveCount(4, "the sample's Tenants requests, its Projects request, the page's two of its own, and the same written as the property");
        var own = DocsExamples.Of(Page, OwnRequirement);

        var host = GeneratorTestHost.Create(TestOnly, "TestOnly.cs");
        for (var index = 0; index < 3; index++)
        {
            host = host.WithSource(Header + "namespace Examples.Tenancy;\n\n" + requests[index], "Requests" + index + ".cs");
        }

        host = host.WithSource(Header + "using Examples.Tenancy;\n\nnamespace Examples.Tenancy.AsProperty;\n\n" + requests[3], "AsProperty.cs");
        for (var index = 0; index < own.Count; index++)
        {
            host = host.WithSource(Header + "namespace Examples.Tenancy;\n\n" + own[index], "Own" + index + ".cs");
        }

        return host.WithAssemblyName("Examples.Tenancy").WithTenancy().WithMembershipAlone().WithMediator().RunCore();
    }

    /// <summary>The module of the boards with its check, or without it, beside the test's stand-ins for Tenancy's answers and check.</summary>
    private static ServiceProvider Services(EmittedAssembly emitted, object plan, object key, object answers, bool withPlanCheck)
    {
        var services = new ServiceCollection();
        if (withPlanCheck)
        {
            emitted.CallStatic("Examples.Tenancy.BoardsModule", "AddBoardsAccess", services);
        }

        services.AddSingleton(emitted.Type("Examples.Tenancy.ITenantPlans"), plan);
        services.AddSingleton(emitted.Type("Examples.Tenancy.KeyOfTheTest"), key);
        services.AddSingleton(answers.GetType().GetInterfaces().Single(each => each.Name.StartsWith("ITenancyAnswers", StringComparison.Ordinal)), answers);
        typeof(AccessCheckServiceCollectionExtensions).GetMethod(nameof(AccessCheckServiceCollectionExtensions.AddAccessCheck))!
            .MakeGenericMethod(emitted.Type("Examples.Tenancy.IBoardsRequest"), emitted.Type("Examples.Tenancy.KeyCheckOfTheTest"))
            .Invoke(null, [services]);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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

    /// <summary>A generated text as the page writes it: the names without the namespaces they are written in full with, the spaces as one.</summary>
    private static string Written(string code)
    {
        foreach (var name in WrittenInFull)
        {
            code = code.Replace("global::" + name, string.Empty, StringComparison.Ordinal);
        }

        return string.Join('\n', code.Replace("global::", string.Empty, StringComparison.Ordinal).ReplaceLineEndings("\n").Split('\n').Select(line => Spaces().Replace(line.Trim(), " ")));
    }

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

    /// <summary>The file of the sample that declares <paramref name="name"/>, in the application project of its module.</summary>
    private static string SampleFileOf(string name)
        => Directory.GetFiles(Path.Combine(Root(), "Examples", "Tenancy", "Modules"), name + ".cs", SearchOption.AllDirectories)
            .Should().ContainSingle(file => file.Contains(".Application", StringComparison.Ordinal), "the sample declares {0} once, in a module's application project", name).Subject;

    /// <summary>The plain csharp blocks of the page: its examples.</summary>
    private static List<string> PlainExamples()
        => [.. PlainCsharp().Matches(Read()).Select(match => match.Groups["code"].Value)];

    private static string Read() => File.ReadAllText(Path.Combine(Root(), "docs", Page)).ReplaceLineEndings("\n");

    private static string Migrations() => Path.Combine(Root(), "Examples", "Tenancy", "supabase", "migrations");

    private static string WalkthroughPath() => Path.Combine(Root(), "website", "src", "components", "RequestWalkthrough", "walkthrough.json");

    private static string Root()
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
