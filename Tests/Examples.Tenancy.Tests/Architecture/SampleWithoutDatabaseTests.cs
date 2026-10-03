using System.Net;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.HotChocolate.Fusion.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// The host the architecture, schema and translation tests read (<see cref="SampleFactory.WithoutDatabase"/>): the
/// sample's host as it is composed on Postgres, with no database behind it. Those tests run in every build because
/// of it, so what it leaves out is held here: exactly the hosted services that ask the database something, which
/// the registrations those tests read still list, and what is left starts and answers with nothing to connect to.
/// </summary>
public sealed class SampleWithoutDatabaseTests
{
    /// <summary>
    /// The hosted services of the host that ask the database something, at start-up or for as long as it runs: the
    /// three start-up checks, each module's outbox poller, and the seeding. One more is a decision: it is left out of
    /// the host these tests read, so no test without a container starts it.
    /// </summary>
    private static readonly Type[] AskTheDatabase =
    [
        typeof(PostgresStartupCheck),
        typeof(TenancyStartupCheck),
        typeof(ProjectFunctionsCheck),
        typeof(OutboxBackgroundService<TenantsContext>),
        typeof(OutboxBackgroundService<ProjectsContext>),
        typeof(OutboxBackgroundService<InspectionsContext>),
        typeof(DemoSeeder),
    ];

    [Fact]
    public async Task The_host_starts_and_answers_with_no_database_behind_it()
    {
        await using var host = SampleFactory.WithoutDatabase();
        using var client = host.CreateClient();

        using var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        health.StatusCode.Should().Be(HttpStatusCode.OK);

        // None of them runs. The gateway's check that the modules' schemas compose still does, and it passed, or
        // the host had not started.
        var running = host.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToList();
        running.Should().NotIntersectWith(AskTheDatabase);
        running.Should().Contain(type => type.Assembly == typeof(InMemoryFusionSchemas).Assembly);

        // Nothing answers at the address the host was given, so a connection anything had asked for would have
        // failed, and the host logs a failure as an error.
        host.Errors.Count.Should().Be(0, "the host logged: {0}", host.Errors.Describe());
    }

    [Fact]
    public async Task What_the_host_leaves_out_is_what_asks_the_database_and_the_registrations_still_list_it()
    {
        await using var host = SampleFactory.WithoutDatabase();
        var running = host.Services.GetServices<IHostedService>().Count();
        var registered = HostRegistrations.All.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IHostedService)).ToList();

        // The architecture tests read the registrations as the host made them, before anything was taken out.
        registered.Select(descriptor => descriptor.ImplementationType).Should().Contain(AskTheDatabase);
        registered.Should().HaveCount(running + AskTheDatabase.Length, "the host without a database runs every hosted service but the ones listed here");
    }
}
