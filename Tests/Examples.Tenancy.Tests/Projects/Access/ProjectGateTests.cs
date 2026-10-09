using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// The one question another module may put to Projects, asked the way such a module asks it: from its own code,
/// in a scope of the request, as the request's seat. The answer must be the one Projects' own commands act on,
/// and a project out of reach must say nothing about itself.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ProjectGateTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static ProjectId PierSeven => Harbor.ProjectNamed("Pier 7").Id;

    private static ProjectId BayBridge => Harbor.ProjectNamed("Bay bridge").Id;

    [Fact]
    public async Task The_gate_answers_what_the_commands_decide()
    {
        var (view, edit, outside) = await AsJunoAsync(async gate => (
            await gate.AskAsync(PierSeven, ProjectKeys.View, Cancellation),
            await gate.AskAsync(PierSeven, ProjectKeys.Edit, Cancellation),
            await gate.AskAsync(BayBridge, ProjectKeys.View, Cancellation)));

        view.Should().Be(new ProjectAnswer(Visible: true, Allowed: true, Closed: false));
        edit.Should().Be(new ProjectAnswer(Visible: true, Allowed: false, Closed: false), "a surveyor on the crew may look, and not rename");
        outside.Should().Be(new ProjectAnswer(Visible: false, Allowed: false, Closed: false), "a project out of reach says nothing about itself");
    }

    [Fact]
    public async Task The_gate_shows_a_member_without_a_role_the_project_and_allows_nothing_more()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var tove = Harbor.SeatOf(DemoPeople.Tove);

        // Tove, whose seat is in South Bay, is put on Pier 7's crew with no role.
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven.Value}/crew", new { seatId = tove.Value }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        ProjectAnswer view, record;
        using (SampleCallers.BeginSeatOf(DemoPeople.Tove, Harbor))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var gate = scope.ServiceProvider.GetRequiredService<IProjectGate>();
            view = await gate.AskAsync(PierSeven, ProjectKeys.View, Cancellation);
            record = await gate.AskAsync(PierSeven, InspectionKeys.Record, Cancellation);
        }

        view.Should().Be(new ProjectAnswer(Visible: true, Allowed: true, Closed: false), "being on the crew is being let in on the project");
        record.Should().Be(new ProjectAnswer(Visible: true, Allowed: false, Closed: false), "and every other key takes a role that grants it");
    }

    [Fact]
    public async Task The_gate_answers_a_projects_planned_range_to_whoever_sees_it_and_to_nobody_else()
    {
        var host = await sample.SharedAsync();
        var depot = Harbor.ProjectNamed("Inland depot").Id;

        // Rhea owns Inland depot, the one planned project. Maud sees it and may not record on it: the plan is
        // answered with the project, whatever the key. Juno does not see it, and is told nothing.
        async Task<ProjectAnswer> AskAsAsync(DemoPerson person)
        {
            using (SampleCallers.BeginSeatOf(person, Harbor))
            {
                await using var scope = host.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IProjectGate>().AskAsync(depot, InspectionKeys.Record, Cancellation);
            }
        }

        var (rhea, maud, juno) = (await AskAsAsync(DemoPeople.Rhea), await AskAsAsync(DemoPeople.Maud), await AskAsAsync(DemoPeople.Juno));

        rhea.Planned.Should().NotBeNull().And.Match<DateRange>(planned => planned.IsValid);
        rhea.Should().Be(new ProjectAnswer(Visible: true, Allowed: true, Closed: false, rhea.Planned));
        maud.Should().Be(new ProjectAnswer(Visible: true, Allowed: false, Closed: false, rhea.Planned));
        juno.Should().Be(new ProjectAnswer(Visible: false, Allowed: false, Closed: false, Planned: null), "a project out of reach says nothing about itself, its plan included");
    }

    [Fact]
    public async Task An_unknown_key_is_refused_for_every_project()
    {
        // A typo in the asking module's code fails for every project, not only for those the caller happens to see.
        var refused = await AsJunoAsync(async gate =>
        {
            var failures = new List<Exception>();
            foreach (var project in new[] { PierSeven, BayBridge, new ProjectId(Guid.NewGuid()) })
            {
                var ask = () => gate.AskAsync(project, "projects.nothing", Cancellation);
                failures.Add((await ask.Should().ThrowAsync<ArgumentException>()).Which);
            }

            return failures;
        });

        refused.Should().HaveCount(3);
    }

    [Fact]
    public async Task The_gate_answers_about_many_projects_in_one_statement_and_leaves_out_those_out_of_reach()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter)));

        // Every project of harbor, one of another tenant and one that does not exist, asked about together. Rhea
        // reaches Pier 7 through her area and Inland depot through its crew, and none of the others.
        ProjectId[] asked = [.. Harbor.Projects.Select(project => project.Id), DemoData.Meadow.ProjectNamed("Garden shed").Id, new ProjectId(Guid.NewGuid())];
        ProjectId[] hers = [PierSeven, Harbor.ProjectNamed("Inland depot").Id];

        using (SampleCallers.BeginSeatOf(DemoPeople.Rhea, Harbor))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var gate = scope.ServiceProvider.GetRequiredService<IProjectGate>();

            counter.WatchThisFlow();
            var answers = await gate.AskAsync(asked, InspectionKeys.Record, Cancellation);

            counter.Commands.Should().ContainSingle("the projects are asked about together, whatever their number");
            answers.Keys.Should().BeEquivalentTo(hers, "a project out of reach is left out, whatever the reason");

            // Each answer is the one the same question about that project alone gets.
            foreach (var project in hers)
            {
                answers[project].Should().Be(await gate.AskAsync(project, InspectionKeys.Record, Cancellation));
            }

            answers[PierSeven].Should().Be(new ProjectAnswer(Visible: true, Allowed: true, Closed: false));

            // The mistakes of the asking module's code fail whatever the projects, even for none.
            var unknown = () => gate.AskAsync([], "projects.nothing", Cancellation);
            var tooMany = () => gate.AskAsync([.. Enumerable.Range(0, IProjectGate.MostProjects + 1).Select(_ => new ProjectId(Guid.NewGuid()))], ProjectKeys.View, Cancellation);
            await unknown.Should().ThrowAsync<ArgumentException>();
            await tooMany.Should().ThrowAsync<ArgumentException>();
            (await gate.AskAsync([], ProjectKeys.View, Cancellation)).Should().BeEmpty();
        }
    }

    /// <summary>Asks the gate of a scope of its own, as Juno in harbor, as a request of hers would.</summary>
    private async Task<T> AsJunoAsync<T>(Func<IProjectGate, Task<T>> ask)
    {
        var host = await sample.SharedAsync();
        using (SampleCallers.BeginSeatOf(DemoPeople.Juno, Harbor))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await ask(scope.ServiceProvider.GetRequiredService<IProjectGate>());
        }
    }
}
