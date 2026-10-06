using System.Net;
using System.Net.Http.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// What holds a command on a project to its check, from the check to the save, on Supabase's Postgres with row level
/// security forced. On the default path, as the sample ships: the request's requirement before the handler, the
/// version its caller named where the handler loads, the version loaded where it saves, the project's own rules,
/// and the database checking the write once more as the caller. Under the expert hold, which a host switches on with
/// one line, <c>UseMemberHolds</c>, every save of a project is held to what its request's check read as well, and a
/// project changed without such a check is not saved.
/// </summary>
/// <remarks>
/// A race is played between the check and the handler: a step of the pipeline, registered after the module's access
/// behavior, sends the change that comes in between, through the routes, as another person's request would.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class AccessHoldScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    /// <summary>
    /// What comes between the check and the handler of a rename of Pier 7, who renames, and what the rename answers on
    /// the default path and under the hold: the status, and the code of a refusal.
    /// </summary>
    public static TheoryData<string, bool, HttpStatusCode, string?> Races => new()
    {
        // Vic is crew lead, and Leo takes the role away: Vic holds no key left that writes a project, so the database
        // refuses his write. Under the hold the crew row moved the project's version on: a lost race.
        { "lead-role-taken", false, HttpStatusCode.Forbidden, ToolkitRefusals.Refused },
        { "lead-role-taken", true, HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict },

        // Rhea reaches Pier 7 through her grant at North, and Ada revokes it: she sees the project no longer, so the
        // handler finds none to load, whichever way.
        { "grant-revoked", false, HttpStatusCode.NotFound, ProjectRefusals.NotFound },
        { "grant-revoked", true, HttpStatusCode.NotFound, ProjectRefusals.NotFound },

        // Ada moves Pier 7 to South Bay, out of Rhea's reach: the same.
        { "moved-out-of-reach", false, HttpStatusCode.NotFound, ProjectRefusals.NotFound },
        { "moved-out-of-reach", true, HttpStatusCode.NotFound, ProjectRefusals.NotFound },

        // Ada moves it to North Inland, where Rhea still holds the key: who she is did not change, so the default
        // path renames it. Under the hold the move was a change to the project since the check: a lost race.
        { "moved-within-reach", false, HttpStatusCode.NoContent, null },
        { "moved-within-reach", true, HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict },

        // Rhea renames it, and Leo named no version: the last write wins on the default path, which is what a caller
        // that names no version asks for. Under the hold it is a lost race.
        { "renamed-meanwhile", false, HttpStatusCode.NoContent, null },
        { "renamed-meanwhile", true, HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict },

        // The same, and Leo named the version he read, which the check found current: the handler loads it at another
        // one, and the If-Match the check passed is held at the load.
        { "renamed-meanwhile-if-match", false, HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict },
        { "renamed-meanwhile-if-match", true, HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict },
    };

    [Theory]
    [MemberData(nameof(Races))]
    public async Task A_change_between_the_check_and_the_handler_is_answered_as_decided(string race, bool holds, HttpStatusCode status, string? code)
    {
        var between = new BetweenCheckAndHandler();
        await using var onPostgres = await sample.StartOnPostgresAsync(services =>
        {
            services.AddSingleton(between);
            services.AddScoped<IPipelineBehavior<ChangeProjectName, Unit>, RaceBeforeRename>();
            if (holds)
            {
                services.HoldProjectSaves();
            }
        });
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        var renamer = leo;
        long? ifMatch = null;
        switch (race)
        {
            case "lead-role-taken":
                renamer = vic;
                (await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Harbor.ProjectRoles[SampleCatalogue.CrewLead])).StatusCode.Should().Be(HttpStatusCode.NoContent);
                between.Once(() => leo.TakeCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Harbor.ProjectRoles[SampleCatalogue.CrewLead]));
                break;

            case "grant-revoked":
                renamer = rhea;
                between.Once(() => ada.DeleteAsync(
                    $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Rhea).Value}/grants/{Harbor.UnitNamed("North").Value}/{Harbor.Roles[SampleCatalogue.AreaManager].Value}", Cancellation));
                break;

            case "moved-out-of-reach":
            case "moved-within-reach":
                renamer = rhea;
                var unit = Harbor.UnitNamed(race == "moved-out-of-reach" ? "South Bay" : "North Inland");
                between.Once(() => ada.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/unit", new { unitId = unit.Value }, Cancellation));
                break;

            case "renamed-meanwhile":
            case "renamed-meanwhile-if-match":
                ifMatch = race == "renamed-meanwhile-if-match" ? (await leo.ProjectDetailAsync(PierSeven)).GetProperty("version").GetInt64() : null;
                between.Once(() => rhea.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/name", new { name = "Meanwhile" }, Cancellation));
                break;
        }

        using var renamed = await RenameAsync(renamer, "Renamed after the check", ifMatch);

        between.Ran.Should().BeTrue("the change came between the check and the handler");
        between.Answer.Should().Be(HttpStatusCode.NoContent, "the change in between was made");
        var problem = await renamed.ProblemAsync();
        renamed.StatusCode.Should().Be(status, "the answer was {0}", problem.Body.GetRawText());
        problem.Code.Should().Be(code);
        (await NameAsync(onPostgres)).Should().Be(
            status == HttpStatusCode.NoContent ? "Renamed after the check" : race.StartsWith("renamed-meanwhile", StringComparison.Ordinal) ? "Meanwhile" : "Pier 7",
            "a refused rename changed nothing");
    }

    [Fact]
    public async Task On_the_default_path_a_handler_called_directly_is_held_by_the_database_to_what_its_caller_may()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();

        // Hana does not see Pier 7: there is nothing to load. Juno sees it, as its surveyor, and holds no key that writes
        // it: the database refuses the write.
        (await DirectRenameAsync(onPostgres, DemoPeople.Hana)).Should().BeOfType<RefusalException>().Which.Code.Should().Be(ProjectRefusals.NotFound);
        (await DirectRenameAsync(onPostgres, DemoPeople.Juno)).Should().BeOfType<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);
        (await NameAsync(onPostgres)).Should().Be("Pier 7");

        // Leo owns it and may: nothing notices that the check was passed by, and on the default path nothing has to.
        (await DirectRenameAsync(onPostgres, DemoPeople.Leo)).Should().BeNull();
        (await NameAsync(onPostgres)).Should().Be("Direct");
    }

    [Fact]
    public async Task Under_the_hold_a_handler_called_directly_saves_nothing()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync(services => services.HoldProjectSaves());

        // Leo may rename Pier 7: what is refused is the way round the check, before anything is written.
        (await DirectRenameAsync(onPostgres, DemoPeople.Leo)).Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("was changed with no request in hand");
        (await NameAsync(onPostgres)).Should().Be("Pier 7");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_seat_s_own_place_is_saved_as_the_application_s_work_only_for_a_command_its_check_let_through(bool holds)
    {
        await using var onPostgres = await sample.StartOnPostgresAsync(services =>
        {
            if (holds)
            {
                services.HoldProjectSaves();
            }
        });
        var vic = Harbor.SeatOf(DemoPeople.Vic);
        var observer = Harbor.ProjectRoles[SampleCatalogue.Observer];
        var lead = Harbor.ProjectRoles[SampleCatalogue.CrewLead];

        // Vic is an observer on Pier 7 and manages no crew. Reached around their checks, the handlers that give up a
        // seat's own place save as Vic, not as the application: on the default path the database refuses his write,
        // and under the hold a change no check let through is refused before anything is written.
        Exception?[] refused =
        [
            await DirectAsync(onPostgres, DemoPeople.Vic, provider => ActivatorUtilities.CreateInstance<TakeCrewRoleHandler>(provider)
                .Handle(new TakeCrewRole(PierSeven.Id, vic, observer), Cancellation)),
            await DirectAsync(onPostgres, DemoPeople.Vic, provider => ActivatorUtilities.CreateInstance<RemoveCrewMemberHandler>(provider)
                .Handle(new RemoveCrewMember(PierSeven.Id, vic), Cancellation)),
        ];
        foreach (var thrown in refused)
        {
            if (holds)
            {
                thrown.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("was changed with no request in hand");
            }
            else
            {
                thrown.Should().BeOfType<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);
            }
        }

        using var leo = await onPostgres.Host.ClientAsync("leo", Harbor.Slug);
        (await RolesOnTheCrewAsync(leo, vic)).Should().Equal([observer.Value], "Vic is on the crew still, with his role");

        // Made crew lead, Vic manages the crew, and through its check gives up his own lead role: that save is the
        // application's work for him, which the database lets through, and the hold holds to what the check read.
        await DoneAsync(leo.GiveCrewRoleAsync(PierSeven, vic, lead), "Vic made crew lead");
        using var asVic = await onPostgres.Host.ClientAsync("vic", Harbor.Slug);
        await DoneAsync(asVic.TakeCrewRoleAsync(PierSeven, vic, lead), "Vic gave up his own lead role");
        (await RolesOnTheCrewAsync(leo, vic)).Should().Equal([observer.Value]);
    }

    [Fact]
    public async Task Under_the_hold_the_application_s_own_work_needs_no_check()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync(services => services.HoldProjectSaves());

        using (TenantsTenancy.BeginSystemIn(Harbor.Id, Harbor.Administrator.Id))
        {
            await using var scope = onPostgres.Host.Services.CreateAsyncScope();
            await new ChangeProjectNameHandler(scope.ServiceProvider.GetRequiredService<IProjectStore>())
                .Handle(new ChangeProjectName(PierSeven.Id, "Renamed by the application"), Cancellation);
        }

        (await NameAsync(onPostgres)).Should().Be("Renamed by the application");
    }

    [Fact]
    public async Task Under_the_hold_every_command_on_a_project_goes_through_its_check_to_its_save()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync(services => services.HoldProjectSaves());
        var host = onPostgres.Host;
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var pier = PierSeven.Id.Value;
        var tove = Harbor.SeatOf(DemoPeople.Tove);
        var juno = Harbor.SeatOf(DemoPeople.Juno);
        var observer = Harbor.ProjectRoles[SampleCatalogue.Observer];

        // Each one checked, loaded, changed and saved as Ada, with nothing of the hold in its handler: the crew's
        // commands, the owner's and the lifecycle's.
        await DoneAsync(ada.PutAsJsonAsync($"/projects/{pier}/name", new { name = "Pier 7, held" }, Cancellation), "renamed");
        await DoneAsync(ada.PostAsJsonAsync($"/projects/{pier}/crew", new { seatId = tove.Value }, Cancellation), "Tove put on the crew");
        await DoneAsync(ada.GiveCrewRoleAsync(PierSeven, tove, observer), "Tove given a role");
        await DoneAsync(ada.TakeCrewRoleAsync(PierSeven, tove, observer), "the role taken from Tove");
        await DoneAsync(ada.DeleteAsync($"/projects/{pier}/crew/{tove.Value}", Cancellation), "Tove taken off the crew");
        await DoneAsync(ada.PutAsJsonAsync($"/projects/{pier}/owner", new { seatId = juno.Value }, Cancellation), "Juno named the owner");
        await DoneAsync(ada.PutAsJsonAsync($"/projects/{pier}/unit", new { unitId = Harbor.UnitNamed("North Inland").Value }, Cancellation), "moved");
        await DoneAsync(ada.PostAsync($"/projects/{pier}/close", null, Cancellation), "closed");
        await DoneAsync(ada.PostAsync($"/projects/{pier}/reopen", null, Cancellation), "reopened");

        // Through GraphQL as well: the mutation's resolver sends the command, and its check and its save meet in it.
        var node = await ada.ProjectNodeIdAsync(PierSeven);
        var answer = await ada.GraphQLDataAsync(
            $$"""
            mutation($id: ID!, $name: String!) {
              projectRename(input: { id: $id, name: $name }) {
                project { name }
                {{SampleGraphQLCalls.Errors}}
              }
            }
            """,
            new { id = node, name = "Pier 7, held over GraphQL" });
        answer.GetProperty("projectRename").GetProperty("project").GetProperty("name").GetString().Should().Be("Pier 7, held over GraphQL");

        var detail = await ada.ProjectDetailAsync(PierSeven);
        detail.GetProperty("state").GetString().Should().Be("open");
        (await NameAsync(onPostgres)).Should().Be("Pier 7, held over GraphQL");
    }

    /// <summary>Asserts that a route answered 204, with what it answered otherwise.</summary>
    private static async Task DoneAsync(Task<HttpResponseMessage> sent, string what)
    {
        using var response = await sent;
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "{0} with the hold on, and the answer was {1}", what, await response.Content.ReadAsStringAsync(Cancellation));
    }

    /// <summary>Renames Pier 7 through its route, with <c>If-Match</c> when a version is named.</summary>
    private static async Task<HttpResponseMessage> RenameAsync(HttpClient client, string name, long? ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/projects/{PierSeven.Id.Value}/name") { Content = JsonContent.Create(new { name }) };
        if (ifMatch is { } version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        }

        return await client.SendAsync(request, Cancellation);
    }

    /// <summary>Renames Pier 7 to "Direct" by calling the handler directly, as <paramref name="person"/>; what it threw, or <see langword="null"/>.</summary>
    private static Task<Exception?> DirectRenameAsync(SampleOnPostgres onPostgres, DemoPerson person)
        => DirectAsync(onPostgres, person, provider => new ChangeProjectNameHandler(provider.GetRequiredService<IProjectStore>())
            .Handle(new ChangeProjectName(PierSeven.Id, "Direct"), Cancellation));

    /// <summary>
    /// Runs a handler directly, past the mediator and its checks, as <paramref name="person"/> in a scope of its own;
    /// what it threw, or <see langword="null"/>.
    /// </summary>
    private static async Task<Exception?> DirectAsync(SampleOnPostgres onPostgres, DemoPerson person, Func<IServiceProvider, ValueTask<Unit>> handle)
    {
        using (SampleCallers.BeginSeatOf(person, Harbor))
        {
            await using var scope = onPostgres.Host.Services.CreateAsyncScope();
            try
            {
                await handle(scope.ServiceProvider);
                return null;
            }
            catch (Exception thrown)
            {
                return thrown;
            }
        }
    }

    /// <summary>The project roles <paramref name="seat"/> holds on Pier 7's crew, as <paramref name="client"/> reads them.</summary>
    private static async Task<IReadOnlyList<Guid>> RolesOnTheCrewAsync(HttpClient client, SeatId seat)
        => [.. (await client.CrewAsync(PierSeven)).EnumerateArray().Single(member => member.GetProperty("seatId").GetGuid() == seat.Value)
            .GetProperty("roles").EnumerateArray().Select(held => held.GetProperty("roleId").GetGuid())];

    /// <summary>Pier 7's name as the database has it, read as the role that owns it.</summary>
    private static Task<string> NameAsync(SampleOnPostgres onPostgres)
        => onPostgres.AsOwnerAsync<string>($"SELECT \"Name\" FROM projects.\"Projects\" WHERE \"Id\" = '{PierSeven.Id.Value}'", Cancellation);

    /// <summary>The change a test plays between the check and the handler of the next rename, once.</summary>
    private sealed class BetweenCheckAndHandler
    {
        private Func<Task<HttpResponseMessage>>? _race;

        /// <summary>Whether the change was played.</summary>
        public bool Ran { get; private set; }

        /// <summary>What the change in between was answered.</summary>
        public HttpStatusCode? Answer { get; private set; }

        /// <summary>Plays <paramref name="race"/> before the next rename's handler, and no other.</summary>
        public void Once(Func<Task<HttpResponseMessage>> race) => _race = race;

        /// <summary>Plays the change, the first time it is asked to: the rename that change sends itself passes by.</summary>
        public async Task RunAsync()
        {
            if (Interlocked.Exchange(ref _race, null) is { } race)
            {
                Ran = true;
                using var answer = await race();
                Answer = answer.StatusCode;
            }
        }
    }

    /// <summary>Registered after the module's behaviors: runs after the access check and right before the handler.</summary>
    private sealed class RaceBeforeRename(BetweenCheckAndHandler between) : IPipelineBehavior<ChangeProjectName, Unit>
    {
        public async ValueTask<Unit> Handle(ChangeProjectName message, MessageHandlerDelegate<ChangeProjectName, Unit> next, CancellationToken cancellationToken)
        {
            await between.RunAsync();
            return await next(message, cancellationToken);
        }
    }
}

