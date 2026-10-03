using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// Work with no caller is refused, and background work runs as system work inside one tenant: nothing gets into
/// a tenant without a Tenancy caller that was begun on purpose, and what was begun stays in its tenant.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Outside any scope there is no caller, which counts as nobody: it reads no tenant's rows and writes none.</item>
/// <item>The toolkit's own system caller is not Tenancy's: it stands for the application's bookkeeping, which is
/// no tenant's, so Tenancy gives it nothing, and the database gives its role no tenant's table.</item>
/// <item>System work in a tenant reads and writes that tenant only.</item>
/// <item>A request runs as its own caller, the toolkit's and Tenancy's, whatever was current when it started.</item>
/// <item>The outboxes are not kept to a tenant, so their pollers need no caller; and Tenancy's own events never
/// leave its module.</item>
/// </list>
/// The tests here that write are refused, so they share one host; the ones that watch the outboxes or change how
/// the host runs ask for their own.
/// <para>
/// The hosts run on Supabase's own Postgres image, where two things keep a tenant's rows apart: the application's
/// tenant filter and save check, and the exported privileges and policies under them. Each is asked for itself
/// here. What the database holds is counted as the role that owns it, which the policies let through. The
/// toolkit's system caller is the role the application's bookkeeping runs as, which the database gives no
/// tenant's table to read at all, so its reads are refused there before any filter matters; that the filter by
/// itself shows that caller nothing is held where it can be seen, in the package's <c>TenantFilterTests</c>.
/// What system work in a tenant is shown past the application's filter is what the policies alone leave it.
/// And a row of another tenant is put before the save check by loading it as that tenant's own work first,
/// since under the policies no other caller finds it. That every entity with a tenant has the filter at all is
/// a fact of the models, which <c>ModuleModelTests</c> holds without a database: no answer here would show a
/// read that lost it, since the database keeps the same rows back.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class FailClosedScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>Every project the database holds, of every tenant, counted past the policies.</summary>
    private const string EveryProject = $"""SELECT count(*) FROM {ProjectsContext.Schema}."{ProjectsContext.ProjectsTable}" """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task Work_outside_any_scope_is_refused()
    {
        var host = await sample.SharedAsync();
        TenancyCallers.Ambient.Should().BeNull("nothing in this test began a Tenancy caller");

        await using var scope = host.Services.CreateAsyncScope();
        var seats = scope.ServiceProvider.GetRequiredService<SampleTenancy.SeatCommands>();

        var suspend = () => seats.SuspendAsync(Harbor.SeatOf(DemoPeople.Leo), Cancellation);

        (await suspend.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(TenancyRefusals.NotSeated);
    }

    [Fact]
    public async Task Core_System_without_TenancyWork_reads_and_writes_nothing()
    {
        var onPostgres = await sample.SharedOnPostgresAsync();
        var pierSeven = Harbor.ProjectNamed("Pier 7");

        // The rows are there: the role that owns the database counts every tenant's.
        (await onPostgres.AsOwnerAsync<long>(EveryProject, Cancellation)).Should().Be(Harbor.Projects.Count + Meadow.Projects.Count);

        using (Callers.Begin(Caller.System))
        {
            await using var scope = onPostgres.Host.Services.CreateAsyncScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
            var tenancy = scope.ServiceProvider.GetRequiredService<TenantsContext>();

            // The toolkit's system caller reads none of them, with the application's filter or past it: the role
            // the bookkeeping runs as holds no privilege on a tenant's table, so the database refuses the statement.
            (await RefusedAsync(() => projects.Projects.ToListAsync(Cancellation))).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            (await RefusedAsync(() => projects.Projects.IgnoreQueryFilters().CountAsync(Cancellation))).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            (await RefusedAsync(() => tenancy.Tenants.ToListAsync(Cancellation))).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            (await RefusedAsync(() => tenancy.Seats.ToListAsync(Cancellation))).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            (await RefusedAsync(() => tenancy.Seats.IgnoreQueryFilters().CountAsync(Cancellation))).Should().Be(PostgresErrorCodes.InsufficientPrivilege);

            // A Tenancy command refuses it, and so does the save check under a module's own write.
            var suspend = () => scope.ServiceProvider.GetRequiredService<SampleTenancy.SeatCommands>().SuspendAsync(Harbor.SeatOf(DemoPeople.Leo), Cancellation);
            (await suspend.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(TenancyRefusals.NotSeated);

            // It finds no project to change, so one is put before it: loaded as harbor's own work, which reads
            // harbor's rows, and changed and saved once that work has ended and the system caller is all there is.
            Project pier;
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id, Harbor.Administrator.Id))
            {
                pier = await projects.Projects.AsTracking().SingleAsync(project => project.Id == pierSeven.Id, Cancellation);
            }

            TenancyCallers.Ambient.Should().BeNull("harbor's work has ended");
            pier.Rename("Pier 7, renamed by the application");
            var save = () => projects.SaveChangesAsync(Cancellation);
            (await save.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(TenancyRefusals.NotSeated);
        }

        (await NameOfAsync(onPostgres, pierSeven)).Should().Be("Pier 7");
    }

    [Fact]
    public async Task SystemInTenant_work_stays_in_its_tenant()
    {
        var onPostgres = await sample.SharedOnPostgresAsync();
        var host = onPostgres.Host;
        var gardenShed = Meadow.ProjectNamed("Garden shed");

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id, Harbor.Administrator.Id))
        {
            // It reads all of harbor, and nothing of meadow.
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
                (await projects.Projects.Select(project => project.Name).ToListAsync(Cancellation))
                    .Should().BeEquivalentTo(Harbor.Projects.Select(project => project.Name));

                // Past the application's filter it is the policies that keep it there: harbor's work is shown
                // harbor's rows, and the shed is not among them.
                (await projects.Projects.IgnoreQueryFilters().Select(project => project.Name).ToListAsync(Cancellation))
                    .Should().BeEquivalentTo(Harbor.Projects.Select(project => project.Name));
            }

            // A meadow project it got hold of anyway is not saved, changed or new. Only meadow's own work finds
            // the shed, so it is loaded as that, and changed and saved when harbor's work is current again.
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
                Project shed;
                using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Meadow.Id, Meadow.Administrator.Id))
                {
                    shed = await projects.Projects.AsTracking().SingleAsync(project => project.Id == gardenShed.Id, Cancellation);
                }

                shed.Rename("Garden shed, renamed from harbor");

                var save = () => projects.SaveChangesAsync(Cancellation);
                (await save.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(TenancyRefusals.OtherTenant);
            }

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
                projects.Projects.Add(new Project(
                    ProjectId.CreateSequential(),
                    Meadow.Id,
                    "M-100",
                    "Greenhouse",
                    Meadow.Root,
                    Meadow.Administrator.Id,
                    Meadow.ProjectRoles[SampleCatalogue.CrewLead],
                    DateTimeOffset.UtcNow));

                var save = () => projects.SaveChangesAsync(Cancellation);
                (await save.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(TenancyRefusals.OtherTenant);
            }
        }

        // Nothing was written: the shed has its name, and the database holds the projects it held.
        (await NameOfAsync(onPostgres, gardenShed)).Should().Be("Garden shed");
        (await onPostgres.AsOwnerAsync<long>(EveryProject, Cancellation)).Should().Be(Harbor.Projects.Count + Meadow.Projects.Count);
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        (await tove.VisibleProjectsAsync()).Names().Should().Equal("Garden shed");
    }

    [Fact]
    public async Task An_ambient_caller_leaking_into_a_request_is_replaced()
    {
        var logs = new RecordingLoggerProvider();
        var recorder = new AmbientCallerRecorder();
        await using var host = await sample.StartAsync(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            recorder.AddTo(services);
        });

        // The test server normally runs a request without the caller's execution context; here it keeps it, so
        // what the test begins really is current when the request starts, as a caller leaked from start-up would
        // be. Set on the server before any client is made, since each client's handler reads it when it is made.
        host.Server.PreserveExecutionContext = true;
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var hers = (await rhea.VisibleProjectsAsync()).Names().ToList();

        IReadOnlyList<string> answered;
        JsonElement me;
        JsonElement seats;
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Meadow.Id))
        {
            answered = [.. (await rhea.VisibleProjectsAsync()).Names()];
            me = await rhea.GetFromJsonAsync<JsonElement>("/me", Cancellation);

            // The one route that asks the toolkit who is calling.
            recorder.Clear();
            seats = await rhea.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);

            Callers.Ambient!.IsSystemIn.Should().BeTrue("the test's own flow still has the system work that leaked");
        }

        // Rhea's own answer: her projects in harbor, as her seat, not everything in meadow as system work.
        answered.Should().Equal(hers).And.NotContain("Garden shed");
        me.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Rhea).Value);

        // Her seat was looked up as nobody, not as the system work in meadow that leaked in.
        recorder.Lookups.Should().NotBeEmpty().And.OnlyContain(
            caller => caller != null && caller.Kind == TenancyCallerKind.Nobody,
            "tenant selection replaced the leaked Tenancy caller before it looked the seat up");

        // And the toolkit's caller inside the request was her user, not the scoped system caller that leaked in.
        recorder.Seen.Should().NotBeEmpty().And.OnlyContain(
            caller => caller != null && caller.Kind == CallerKind.User && caller.UserId == DemoPeople.Rhea.Id,
            "the request made its own caller current");
        seats.EnumerateArray().Select(seat => seat.GetProperty("tenant").GetProperty("slug").GetString()).Should().Equal(Harbor.Slug);

        logs.Entries.Should().Contain(
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Tenancy caller was current", StringComparison.Ordinal),
            "the leaked Tenancy caller reached the request, and tenant selection replaced it");
        logs.Entries.Should().Contain(
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("SystemIn caller was current", StringComparison.Ordinal),
            "and so did the toolkit's caller, which the request replaced with its own");
    }

    [Fact]
    public async Task The_request_caller_is_current_for_the_request()
    {
        var recorder = new AmbientCallerRecorder();
        await using var host = await sample.StartAsync(recorder.AddTo);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var nobody = host.Client(token: null, tenant: null);

        recorder.Clear();
        var seats = await rhea.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);

        // Begun for the request, from its token: the accessor is not what makes it the caller. The route asks it
        // who is calling, and so does every connection the request opens, for the role its statements run as:
        // each time, the request's own caller was the one that was current.
        seats.GetArrayLength().Should().BeGreaterThan(0);
        recorder.Seen.Should().NotBeEmpty().And.OnlyContain(caller => caller != null && caller.Kind == CallerKind.User && caller.UserId == DemoPeople.Rhea.Id);

        // A request without a token is refused by the route, which needs a signed-in person.
        using (var anonymous = await nobody.GetAsync("/me/seats", Cancellation))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // What tenant selection makes current for such a request is the anonymous caller, not the system: run
        // on a request of its own, with no token, in front of a step that reads what is current.
        Caller? current = null;
        ITenancyCaller? tenancy = null;
        var pipeline = new ApplicationBuilder(host.Services);
        pipeline.UseTenantSelection();
        pipeline.Run(_ =>
        {
            (current, tenancy) = (Callers.Ambient, TenancyCallers.Ambient);
            return Task.CompletedTask;
        });
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await pipeline.Build()(new DefaultHttpContext { RequestServices = scope.ServiceProvider });
        }

        current.Should().BeSameAs(Caller.Anonymous);
        tenancy.Should().NotBeNull().And.Match<ITenancyCaller>(caller => caller.Kind == TenancyCallerKind.Nobody);
    }

    [Fact]
    public async Task The_outboxes_drain_with_no_tenancy_caller()
    {
        var sink = new RecordingSink();
        await using var host = await sample.StartAsync(sink.AddTo);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        await Outboxes.DrainedAsync(host, Cancellation);

        // By the host's own clock, which stamps the rows.
        var before = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        // A grant, a rename and an inspection: one change in each module's outbox.
        using (var granted = await rhea.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
            new { unitId = Harbor.UnitNamed("North Coast").Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
            Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var renamed = await leo.PutAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/name", new { name = "Pier 7 east" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var recorded = await juno.PostAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // The pollers run in the background, where no Tenancy caller was ever begun.
        var outboxes = await Outboxes.DrainedAsync(host, Cancellation);

        foreach (var (outbox, rows) in outboxes)
        {
            rows.Should().Contain(row => row.CreatedAt >= before, "{0} stored the change's events", outbox);
            rows.Should().OnlyContain(row => row.ProcessedAt != null, "{0}'s poller marked every row handled", outbox);
        }

        sink.Offered.Should().NotBeEmpty().And.OnlyContain(offered => offered.TenancyCaller == null, "delivery runs with no Tenancy caller");
    }

    [Fact]
    public async Task Tenancy_domain_events_are_not_offered_to_other_modules()
    {
        var sink = new RecordingSink();
        await using var host = await sample.StartAsync(sink.AddTo);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        // The host starts on a demonstration another host seeded, whose events that host's sinks were offered.
        // So each module stores an event here, and what this host's sink is offered is held against those.
        var before = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        using (var granted = await rhea.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
            new { unitId = Harbor.UnitNamed("North Coast").Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
            Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var renamed = await leo.PutAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/name", new { name = "Pier 7 east" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var recorded = await juno.PostAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var outboxes = await Outboxes.DrainedAsync(host, Cancellation);
        List<string> StoredHere(string outbox) => [.. outboxes[outbox].Where(row => row.CreatedAt >= before).Select(row => row.EventName).Distinct()];

        // Tenancy stored its events and its poller handled every one: they were there to be offered, and were not.
        StoredHere(TenantsContext.OutboxTable).Should().NotBeEmpty().And.OnlyContain(name => name.StartsWith("tenancy.", StringComparison.Ordinal));

        var offered = sink.Offered.Select(message => message.Name).Distinct().ToList();
        offered.Should().NotContain(name => name.StartsWith("tenancy.", StringComparison.Ordinal));
        offered.Should().Contain(StoredHere(ProjectsContext.OutboxTable), "Projects' events are offered as they stand").And.Contain("projects.project-renamed");
        offered.Should().Contain(StoredHere(InspectionsContext.OutboxTable), "and so are Inspections'").And.Contain(name => name.StartsWith("inspections.", StringComparison.Ordinal));
    }

    /// <summary>The SQLSTATE the database refuses <paramref name="read"/> with.</summary>
    private static async Task<string> RefusedAsync<T>(Func<Task<T>> read)
        => (await read.Should().ThrowAsync<PostgresException>()).Which.SqlState;

    /// <summary>The name the database holds for <paramref name="project"/>, read as the role that owns it.</summary>
    private static Task<string> NameOfAsync(SampleOnPostgres onPostgres, DemoProject project)
        => onPostgres.AsOwnerAsync<string>(
            $"""SELECT "Name" FROM {ProjectsContext.Schema}."{ProjectsContext.ProjectsTable}" WHERE "Id" = '{project.Id.Value}'""",
            Cancellation);
}
