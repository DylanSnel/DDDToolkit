using DDDToolkit.EntityFramework.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Examples.Tenancy.Tests.Supabase;

/// <summary>
/// The database a test's host starts on. The demonstration is seeded once in a run, by a host that then stopped,
/// and every other host starts on a copy of what that one left, with seeding off. Held here is what makes such a
/// host the same as one that seeded the demonstration itself: the data is in its database before it starts, no
/// event of the seeding is left there for its own pollers, and it runs on the clock the demonstration was seeded
/// under. A host that cannot, because the test gives it a clock, asks for a database with nothing in it.
/// </summary>
/// <remarks>
/// A mistake in the fixture would otherwise show as something else, in some other test: an event left in the
/// copied outboxes is sent again by every host of the run, and a host on another clock compares the
/// demonstration's periods with moments of its own.
/// <para>
/// They need Docker and Supabase's Postgres image, so they carry the samples' traits and stay out of the runs
/// that have neither.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class SeededOnceTests(SampleSupabaseStack stack)
{
    /// <summary>The three modules' outboxes, as a statement names them.</summary>
    private static readonly string[] OutboxTables =
    [
        $"{TenantsContext.Schema}.\"{TenantsContext.OutboxTable}\"",
        $"{ProjectsContext.Schema}.\"{ProjectsContext.OutboxTable}\"",
        $"{InspectionsContext.Schema}.\"{InspectionsContext.OutboxTable}\"",
    ];

    private static readonly string ProjectsTable = $"{ProjectsContext.Schema}.\"{ProjectsContext.ProjectsTable}\"";

    private static readonly int ProjectsOfTheDemonstration = DemoData.Tenants.Sum(tenant => tenant.Projects.Count);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hosts_database_holds_the_demonstration_before_the_host_starts_and_no_event_waits_in_it()
    {
        // Asked for and not yet started: what the owner reads here came with the copy.
        await using var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation);
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);

        (await RowsInAsync(owner, ProjectsTable)).Should().Be(ProjectsOfTheDemonstration, "every project of the demonstration is there");

        var stored = 0L;
        foreach (var outbox in OutboxTables)
        {
            stored += await RowsInAsync(owner, outbox);
            (await RowsInAsync(owner, $"{outbox} WHERE \"{nameof(OutboxMessage.ProcessedAt)}\" IS NULL"))
                .Should().Be(0, "the host that seeded stopped only when {0} was handled, so no host on a copy sends its events again", outbox);
        }

        stored.Should().BePositive("the seeding went through the use cases, which store their events");

        // The host is told to seed nothing, and answers from what is there.
        _ = sample.Host.Server;
        await sample.WaitUntilTheDatabaseIsPastAsync(DateTimeOffset.UtcNow, Cancellation);

        sample.Host.Services.GetRequiredService<IConfiguration>().GetValue<bool>(DemoSeeder.Setting).Should().BeFalse();
        using var tove = await sample.Host.ClientAsync("tove", DemoData.Harbor.Slug);
        (await tove.VisibleProjectsAsync()).Names().Should().Equal("Bay bridge");
    }

    [Fact]
    public async Task A_host_on_the_seeded_demonstration_takes_no_clock_of_its_own()
    {
        // The demonstration's periods were measured from the database's clock when it was seeded. A host that
        // stamped and compared with another clock would be a different application on the same rows.
        await using var sample = await SampleOnPostgres.CreateAsync(
            stack, Cancellation, services: services => services.AddSingleton<TimeProvider>(new StoppedClock()));

        var refused = sample.Host.RefusedStart();

        refused.Select(failure => failure.Message)
            .Should().Contain(message => message.Contains("seeded: false", StringComparison.Ordinal), "the host gave up with {0}", string.Join(" / ", refused.Select(failure => failure.Message)));
    }

    [Fact]
    public async Task A_host_asked_for_without_the_demonstration_starts_on_an_empty_database_and_seeds_it_itself()
    {
        // The way a host with a clock of its own is asked for: the exported files applied, and nothing in the tables.
        await using var sample = await SampleOnPostgres.CreateAsync(
            stack, Cancellation, services: services => services.AddSingleton<TimeProvider>(new StoppedClock()), seeded: false);
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);

        (await RowsInAsync(owner, ProjectsTable)).Should().Be(0);

        // In Development it seeds before it answers, as every start of the sample does, under the clock it was given.
        _ = sample.Host.Server;

        sample.Host.Services.GetRequiredService<IConfiguration>().GetValue<bool>(DemoSeeder.Setting).Should().BeTrue();
        (await RowsInAsync(owner, ProjectsTable)).Should().Be(ProjectsOfTheDemonstration);
    }

    /// <summary>How many rows <paramref name="table"/> has, with the condition written behind it when there is one.</summary>
    private static async Task<long> RowsInAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync(Cancellation))!;
    }
}
