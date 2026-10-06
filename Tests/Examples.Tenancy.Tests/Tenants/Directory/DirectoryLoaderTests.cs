using FluentAssertions;
using GreenDonut;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Tenants.Directory;

/// <summary>
/// The data loader behind a lookup never asks the directory about more ids than one question takes: the
/// directory refuses a longer list, and a wide page of references is no reason to refuse anybody.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DirectoryLoaderTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task More_ids_than_one_question_takes_are_asked_in_parts()
    {
        var sent = new SentRequests();
        await using var host = await sample.StartAsync(sent.AddTo);

        // The loader as the schema registered it. It is the module's own, so it is found by its name, and used
        // through the interface every data loader has.
        var registered = typeof(TenantsModule).Assembly.GetType("Examples.Tenancy.Tenants.Api.Directory.GraphQL.ISeatByIdDataLoader", throwOnError: true)!;
        var most = TenantsTenancy.TenancyDirectory.MostIds;
        SeatId[] ids = [.. Enumerable.Range(0, most).Select(_ => SeatId.CreateSequential()), .. Harbor.Seats.Select(seat => seat.Id)];

        using (SampleCallers.BeginSeatOf(DemoPeople.Juno, Harbor))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var seats = (IDataLoader<SeatId, SeatListing>)scope.ServiceProvider.GetRequiredService(registered);
            sent.Clear();

            var found = await seats.LoadAsync(ids, Cancellation);

            // Nothing was refused, the seats that are there were found, and an id of nothing is nothing.
            found.Should().HaveCount(ids.Length);
            found.OfType<SeatListing>().Select(seat => seat.Id).Should().BeEquivalentTo(Harbor.Seats.Select(seat => seat.Id));
        }

        sent.Of<SeatsById>().Select(asked => asked.Ids.Count).Should().Equal([most, Harbor.Seats.Count], "one batch of the loader is as many questions as it takes");
    }
}
