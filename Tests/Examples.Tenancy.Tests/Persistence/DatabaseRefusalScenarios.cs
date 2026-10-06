using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Persistence;

/// <summary>
/// A rule a command checks first and a unique index keeps as well: when another command gets in between the check
/// and the save, the index answers with the refusal the check gives. Each scenario lets the second command pass
/// its check, commits the first one right before the second saves, and reads the answer.
/// </summary>
/// <remarks>
/// Each scenario writes, so each runs a host of its own. Which index refused is read from the failure the
/// provider gives, and the answer is the refusal the check gives.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DatabaseRefusalScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static readonly DemoTenant Harbor = DemoData.Harbor;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_projects_opened_with_one_number_at_once_answer_number_taken()
    {
        var between = new BetweenCheckAndSave<Project>();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(between)));
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var north = Harbor.UnitNamed("North").Value;

        // The second request has found the number free and is about to save when the first one commits.
        between.Run(async () =>
        {
            using var first = await ada.PostAsJsonAsync("/projects", new { number = "P-777", name = "Got there first", unitId = north }, Cancellation);
            first.StatusCode.Should().Be(HttpStatusCode.Created);
        });

        using var second = await ada.PostAsJsonAsync("/projects", new { number = "P-777", name = "A moment later", unitId = north }, Cancellation);

        between.Ran.Should().BeTrue("the second project passed the check and met the index");
        var refused = await second.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.NumberTaken);
        refused.Argument("Number").Should().Be("P-777");
        refused.Title.Should().Be(ProjectRefusals.Of(ProjectRefusals.NumberTaken, ("Number", "P-777")).Message, "the index gives the text the command gives");

        // And the command's own check gives the same answer to whoever comes after.
        using var third = await ada.PostAsJsonAsync("/projects", new { number = "P-777", name = "Much later", unitId = north }, Cancellation);
        var checkedFirst = await third.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.NumberTaken);
        checkedFirst.Title.Should().Be(refused.Title, "a client cannot tell which of the two refused");
        checkedFirst.Body.GetProperty("arguments").GetRawText().Should().Be(refused.Body.GetProperty("arguments").GetRawText());

        (await ada.VisibleProjectsAsync()).Count(project => project.Text("number") == "P-777").Should().Be(1);
    }

    [Fact]
    public async Task A_slug_taken_in_a_race_answers_slug_taken()
    {
        var between = new BetweenCheckAndSave<Tenant>();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(between)));

        // The second provisioning has found the slug free and is about to save when the first one commits.
        between.Run(() => ProvisionAsync(host, "quarry", "Quarry, first"));

        var refusal = (await FluentActions.Awaiting(() => ProvisionAsync(host, "quarry", "Quarry, a moment later")).Should().ThrowAsync<RefusalException>()).Which;

        between.Ran.Should().BeTrue("the second tenant passed the check and met the index");
        refusal.Code.Should().Be(TenancyRefusals.SlugTaken);
        refusal.Kind.Should().Be(RefusalKind.Conflict);
        refusal.Message.Should().Be(TenancyRefusals.Of(TenancyRefusals.SlugTaken, ("Slug", "quarry")).Message);
        refusal.Arguments.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Slug"] = "quarry" });
        refusal.InnerException.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeAssignableTo<DbException>("the database's index refused it");

        // The first one is there, and whoever comes after gets the same answer, whichever of the two gave it: the
        // command's own check where work outside a tenant may read the tenants, the index where it may not.
        var later = (await FluentActions.Awaiting(() => ProvisionAsync(host, "quarry", "Quarry, much later")).Should().ThrowAsync<RefusalException>()).Which;
        later.Code.Should().Be(TenancyRefusals.SlugTaken);
        later.Message.Should().Be(refusal.Message, "a caller cannot tell which of the two refused");
    }

    /// <summary>Provisions a flat tenant as system work, the way the seeder and an operator's command do.</summary>
    private static async Task ProvisionAsync(SampleFactory host, string slug, string name)
    {
        using (TenantsTenancy.BeginSystem())
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TenantsTenancy.TenantCommands>().ProvisionAsync(
                new TenantsTenancy.TenantToProvision(
                    slug, name, TenantShape.Flat, name, Guid.NewGuid(), "Its administrator", ConfigureRoot: root => root.SetKind(DemoTenant.RootKind)),
                Cancellation);
        }
    }

    /// <summary>
    /// Runs a test's code once, when a context it is added to saves a new <typeparamref name="TAdded"/>, before
    /// anything is written: after the saving command made its checks and before its transaction begins, which is
    /// where a racing command commits. Any other save of the context, the outbox's own among them, passes by.
    /// </summary>
    private sealed class BetweenCheckAndSave<TAdded> : SaveChangesInterceptor
        where TAdded : class
    {
        private Func<Task>? _race;

        /// <summary>Whether the armed code ran.</summary>
        public bool Ran { get; private set; }

        /// <summary>Runs <paramref name="race"/> at the next save that adds one, and not at the save it makes itself.</summary>
        public void Run(Func<Task> race) => _race = race;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<TAdded>().Any(entry => entry.State == EntityState.Added)
                && Interlocked.Exchange(ref _race, null) is { } race)
            {
                Ran = true;
                await race();
            }

            return result;
        }
    }
}
