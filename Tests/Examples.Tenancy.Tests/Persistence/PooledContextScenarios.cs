using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Outbox;
using Examples.Hosting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Persistence;

/// <summary>
/// Requests against a host whose contexts come from a pool, through the routes a client calls. A pool hands one
/// context instance to one request after another, so everything a request must not inherit from the one before
/// is asked here: another caller's tenant, what a refused command had tracked, and the events of a change.
/// </summary>
/// <remarks>
/// Each scenario watches every context the host makes through a <see cref="RentalLog"/> added to the modules'
/// options, so it asks for a host of its own.
/// <para>
/// A context has two pools on Postgres, one over the connections requests are answered on and one over the
/// background's, and which one a rental draws on is decided when it is made, from who is calling then: the
/// toolkit's system caller, which the pollers are, takes from the background's, and everyone else from the
/// requests'. So a request is never handed an instance a poller used, and the instance a poller gave back is
/// found again only by renting as the system caller.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class PooledContextScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static readonly DemoTenant Harbor = DemoData.Harbor;

    private static readonly DemoTenant Meadow = DemoData.Meadow;

    /// <summary>
    /// The connections a host is given for requests when twenty-five clients ask at once: one each and a few to
    /// spare, where the hosts of the other tests get by on a handful.
    /// </summary>
    private static readonly Dictionary<string, string> ConnectionsForManyClients = new()
    {
        [SampleStorage.PoolsSection + ":" + nameof(PostgresPoolBudget.Requests)] = "32",
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requests_of_two_tenants_side_by_side_each_see_their_own()
    {
        var log = new RentalLog();
        await using var host = await WatchedAsync(log, ConnectionsForManyClients);

        // Who asks in harbor, and the projects each of them sees there. In meadow it is always Tove, who has a seat
        // in both tenants and sees Garden shed there.
        (string Person, string[] Sees)[] inHarbor =
        [
            ("ada", ["Bay bridge", "HQ refit", "Inland depot", "Pier 7"]),
            ("rhea", ["Inland depot", "Pier 7"]),
            ("leo", ["Pier 7"]),
            ("tove", ["Bay bridge"]),
            ("juno", ["Pier 7"]),
        ];
        var gardenShed = Meadow.ProjectNamed("Garden shed");
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var person in inHarbor.Select(asking => asking.Person))
        {
            tokens[person] = await host.SignInAsync(person);
        }

        log.Clear();

        // Twenty-five clients at once, twenty requests each. A client switches tenant with every request, and
        // between the list and one project with every other: whichever context a request is handed, and whoever
        // had it the moment before, the answer is the caller's own tenant's and nothing of the other. The clients
        // are made here, one after another: the factory that makes them is not for twenty-five callers at once.
        var asking = Enumerable.Range(0, 25)
            .Select(index => (Index: index, Harbor: host.Client(tokens[inHarbor[index % inHarbor.Length].Person], Harbor.Slug), Meadow: host.Client(tokens["tove"], Meadow.Slug)))
            .ToList();
        var clients = asking.Select(client => Task.Run(async () =>
        {
            var index = client.Index;
            var (person, sees) = inHarbor[index % inHarbor.Length];
            using var harbor = client.Harbor;
            using var meadow = client.Meadow;
            var answered = 0;

            for (var request = 0; request < 20; request++)
            {
                var inMeadow = (index + request) % 2 == 0;
                var client = inMeadow ? meadow : harbor;
                string[] expected = inMeadow ? ["Garden shed"] : sees;

                if (request / 2 % 2 == 0)
                {
                    (await client.VisibleProjectsAsync()).Names().Should().BeEquivalentTo(expected, "{0} lists the projects of {1}", inMeadow ? "tove" : person, inMeadow ? "meadow" : "harbor");
                }
                else
                {
                    // One project of the tenant the request names, which is found, and the same request for a project
                    // of the other tenant, which does not exist there.
                    var own = inMeadow ? gardenShed : Harbor.ProjectNamed(sees[request % sees.Length]);
                    var others = inMeadow ? Harbor.ProjectNamed("Bay bridge") : gardenShed;

                    (await client.ProjectDetailAsync(own)).GetProperty("name").GetString().Should().Be(own.Name);
                    using var notHere = await client.GetAsync($"/projects/{others.Id.Value}", Cancellation);
                    await notHere.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
                }

                answered++;
            }

            return answered;
        }));

        (await Task.WhenAll(clients)).Sum().Should().Be(500);

        // Every one of those requests read on contexts from the pools, and far fewer instances served them than
        // there were rentals: a context a request gave back was the next one's.
        var uses = log.Uses;
        uses.Should().NotBeEmpty().And.OnlyContain(use => use.Pooled);
        log.Rentals.Count.Should().BeGreaterThanOrEqualTo(500, "each request took at least one context");
        log.Instances.Count.Should().BeLessThan(log.Rentals.Count / 4, "the pools handed their contexts out again");
    }

    [Fact]
    public async Task A_refused_command_leaves_nothing_for_the_next_request()
    {
        var log = new RentalLog();
        var between = new BeforeTheSaveOfANewProject();
        await using var host = await sample.StartAsync(services =>
        {
            services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(log));
            services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(log, between));
            services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(log));
        });
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var north = Harbor.UnitNamed("North").Value;
        var depot = Harbor.ProjectNamed("Inland depot");
        await ProjectsOutboxDrainedAsync(host);

        // Ada's command finds the number free, makes the project and is about to save it when another request
        // commits a project with that number. The save is refused by the unique index: by then the request's
        // context tracks the new project, its crew and the event of its opening.
        between.Run(async () =>
        {
            using var first = await ada.PostAsJsonAsync("/projects", new { number = "P-777", name = "Got there first", unitId = north }, Cancellation);
            first.StatusCode.Should().Be(HttpStatusCode.Created);
        });
        using (var second = await ada.PostAsJsonAsync("/projects", new { number = "P-777", name = "Came second", unitId = north }, Cancellation))
        {
            await second.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.NumberTaken);
        }

        var refused = between.Refused;
        refused.Should().NotBeNull("the second project passed the check and met the index");
        between.Tracked.Should().BeGreaterThan(0, "the refused request's context tracked what it was about to save");

        // Its context went back to the pool requests draw on. Another seat's command follows, a request of its own,
        // which is handed that very instance as its unit of work and saves on it: the pool is left with no other
        // idle context to hand out. The test rents as nobody in particular, which is that same pool; the pollers
        // rent from the background's and never take the instance. Should the save land on another instance all
        // the same, the command is sent once more.
        var factory = host.Services.GetRequiredService<IDbContextFactory<ProjectsContext>>();
        log.Clear();
        var renames = 0;
        while (!log.Uses.Any(use => use.IsSave && use.Caller?.Kind == CallerKind.User && ReferenceEquals(use.Context, refused)))
        {
            renames.Should().BeLessThan(20, "the pool hands the refused request's context out again");
            await using (await factory.LeaveOnlyAsync(refused!, Cancellation))
            {
                using var renamed = await rhea.PutAsJsonAsync($"/projects/{depot.Id.Value}/name", new { name = $"Inland depot, take {++renames}" }, Cancellation);
                renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }
        }

        // Only what the two commands that succeeded changed is there: one project with the number, under the name of
        // the one that got there first, and the depot under its last name.
        var projects = await ada.VisibleProjectsAsync();
        projects.Where(project => project.Text("number") == "P-777").Names().Should().Equal("Got there first");
        projects.Names().Should().NotContain("Came second");
        projects.Named($"Inland depot, take {renames}").GetProperty("id").GetGuid().Should().Be(depot.Id.Value);

        // And only their events: one project opened, and one rename for every command that renamed. The event of the
        // project that was refused left with it.
        var stored = (await ProjectsOutboxDrainedAsync(host)).Select(row => row.EventName).ToList();
        stored.Count(name => name == "projects.project-opened").Should().Be(Harbor.Projects.Count + Meadow.Projects.Count + 1, "the seeded projects and the one that got there first");
        stored.Count(name => name == "projects.project-renamed").Should().Be(renames);
    }

    [Fact]
    public async Task A_command_s_events_are_stored_through_the_pooled_context()
    {
        var log = new RentalLog();
        await using var host = await WatchedAsync(log);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        await ProjectsOutboxDrainedAsync(host);
        log.Clear();

        // A project opened through its route.
        using var opened = await ada.PostAsJsonAsync("/projects", new { number = "P-900", name = "Quay wall", unitId = Harbor.Root.Value }, Cancellation);
        opened.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await opened.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();

        // One save by the signed-in user, on a context taken from the pool: the request's own, bound to its scope.
        var save = log.Uses.Where(use => use.IsSave && use.Context is ProjectsContext && use.Caller?.Kind == CallerKind.User).Should().ContainSingle("the command saves once").Which;
        save.Pooled.Should().BeTrue();

        // That save stored the event with the project, and the poller marks it handled.
        var rows = await ProjectsOutboxDrainedAsync(host);
        var row = rows.Should().ContainSingle(candidate => candidate.EventName == "projects.project-opened" && candidate.Payload.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)).Which;
        row.ProcessedAt.Should().NotBeNull("the poller handled it");
    }

    [Fact]
    public async Task The_poller_s_context_comes_from_the_pool_and_goes_back()
    {
        var log = new RentalLog();
        await using var host = await WatchedAsync(log);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        await ProjectsOutboxDrainedAsync(host);
        log.Clear();

        // Something for Projects' poller to handle.
        using (var renamed = await ada.PutAsJsonAsync($"/projects/{Harbor.ProjectNamed("HQ refit").Id.Value}/name", new { name = "HQ refit, second floor" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await ProjectsOutboxDrainedAsync(host)).Should().OnlyContain(row => row.ProcessedAt != null);

        // The poller is the one that marks rows of the outbox handled: an UPDATE of that table, sent as the system.
        var handled = log.Uses
            .Where(use => use.Context is ProjectsContext
                          && use.Statement is { } statement
                          && statement.Contains($"UPDATE {ProjectsContext.Schema}.\"{ProjectsContext.OutboxTable}\"", StringComparison.Ordinal))
            .ToList();
        var poller = handled.Should().NotBeEmpty("the poller marked the event handled").And.Subject.First();
        poller.Pooled.Should().BeTrue("the poller's scope takes its context from a pool, as a request's does");
        poller.Caller.Should().NotBeNull().And.Match<Caller>(caller => caller.IsSystem, "the poller runs as the application itself");

        // No request was served by that instance: the pool it came from is the background's, which a request
        // never rents from.
        log.Uses.Where(use => use.Caller is not { IsSystem: true }).Select(use => use.Context)
            .Should().NotBeEmpty().And.NotContain(poller.Context, "a request's contexts come from the pool over the connections requests are answered on");

        // Its round is over, and the instance it used is back in that pool: rented as the system caller, which
        // is who draws on it, the factory hands it out again.
        var factory = host.Services.GetRequiredService<IDbContextFactory<ProjectsContext>>();
        using (Callers.Begin(Caller.System))
        {
            (await factory.HandsOutAgainAsync(poller.Context, Cancellation)).Should().BeTrue("the poller's scope ended, which gave its context back");
        }
    }

    /// <summary>
    /// A host of the test's own, started on the demonstration, with <paramref name="log"/> on every context of
    /// every module.
    /// </summary>
    private Task<SampleFactory> WatchedAsync(RentalLog log, IReadOnlyDictionary<string, string>? settings = null)
        => sample.StartAsync(
            services =>
            {
                services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(log));
                services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(log));
                services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(log));
            },
            settings);

    /// <summary>
    /// Waits until every outbox has handled every row it holds, and returns Projects' rows: a test that went on
    /// before would count the pollers' work as its own.
    /// </summary>
    private static async Task<IReadOnlyList<OutboxMessage>> ProjectsOutboxDrainedAsync(SampleFactory host)
        => (await Outboxes.DrainedAsync(host, Cancellation))[ProjectsContext.OutboxTable];

    /// <summary>
    /// Runs a test's code once, when a context it is added to saves a new project, before anything is written:
    /// after the command made its checks and before its transaction begins, which is where a racing command
    /// commits. It keeps the context that was saving, and how much it tracked then.
    /// </summary>
    private sealed class BeforeTheSaveOfANewProject : SaveChangesInterceptor
    {
        private Func<Task>? _race;

        /// <summary>The context whose save met the race; <see langword="null"/> until one did.</summary>
        public DbContext? Refused { get; private set; }

        /// <summary>How many entities that context tracked when it began to save.</summary>
        public int Tracked { get; private set; }

        /// <summary>Runs <paramref name="race"/> at the next save that adds a project, and not at the save it makes itself.</summary>
        public void Run(Func<Task> race) => _race = race;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<Project>().Any(entry => entry.State == EntityState.Added)
                && Interlocked.Exchange(ref _race, null) is { } race)
            {
                Refused = context;
                Tracked = context.ChangeTracker.Entries().Count();
                await race();
            }

            return result;
        }
    }
}
