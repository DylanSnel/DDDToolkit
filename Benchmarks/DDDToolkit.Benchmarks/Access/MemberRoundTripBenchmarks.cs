using BenchmarkDotNet.Attributes;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Benchmarks.Infrastructure;
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.TestHost;
using DDDToolkit.Supporting.Membership.TestHost.Documents;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Benchmarks.Access;

/// <summary>
/// The checks of requests that require a permission on one document, on two documents with a marker on each, and on the
/// same two documents with one marker on a list, in a scope that already has its context. The caller manages every
/// document, so each check passes. A marker on each document reads each document in a statement of its own, one after
/// the other. A marker on the list reads both in one statement. On SQLite in memory a statement costs no round trip; on
/// Postgres in a container, every statement is one.
/// </summary>
[MemoryDiagnoser]
public class MemberRoundTripBenchmarks
{
    private static readonly Guid Owner = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    private static readonly Guid Manager = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    private static readonly UserId With = new(Guid.Parse("c0c00000-0000-4000-8000-000000000003"));

    private static readonly DocumentId First = new(Guid.Parse("d0c00000-0000-4000-8000-000000000001"));

    private static readonly DocumentId Second = new(Guid.Parse("d0c00000-0000-4000-8000-000000000002"));

    private readonly ShareDocument _one = new(First, With);
    private readonly ShareOnBoth _both = new(First, Second, With);
    private readonly ShareOnEach _each = new([First, Second], With);

    private BenchmarkServer _server = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private AccessChecks<IFilingRequest> _checks = null!;
    private AccessManager _access = null!;

    /// <summary>The database the checks read.</summary>
    [Params(BenchmarkServerKind.Sqlite, BenchmarkServerKind.Postgres)]
    public BenchmarkServerKind Server { get; set; }

    /// <summary>Starts the database, creates the two documents, each shared with the manager, and a container with the test host's Membership.</summary>
    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    /// <summary>Disposes the scope and the container, and stops the database.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _services.Dispose();
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>The permission that shares, on one document: one statement.</summary>
    [Benchmark(Baseline = true)]
    public Task OneDocument() => RequireAsync(_one);

    /// <summary>The permission that shares, on two documents with a marker on each: two statements, one after the other.</summary>
    [Benchmark]
    public Task TwoMarkers() => RequireAsync(_both);

    /// <summary>The permission that shares, on the same two documents with one marker on the list: one statement.</summary>
    [Benchmark]
    public Task OneMarkerOnTheList() => RequireAsync(_each);

    private async Task RequireAsync(IFilingRequest request)
    {
        using var caller = _access.BeginAs(CurrentUserSnapshot.User(Manager));
        await _checks.RequireAsync(request, CancellationToken.None);
    }

    private async Task SetupAsync()
    {
        _server = await BenchmarkServer.StartAsync(Server);

        var collection = new ServiceCollection();
        collection.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, _, _) => Task.CompletedTask));
        FilingHost.Add(collection);
        collection.AddDbContext<FilingContext>((services, options) => _server.Use(options).UseDDDToolkit(services));
        _services = collection.BuildServiceProvider();
        _access = _services.GetRequiredService<AccessManager>();

        using (_access.BeginAs(CurrentUserSnapshot.System))
        {
            await using var scope = _services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<FilingContext>();
            await context.Database.EnsureCreatedAsync();

            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { First, Second })
            {
                var document = new Document(id, "Document", new UserId(Owner), now.AddDays(-30));
                document.ShareWith(new UserId(Manager), DocumentMembership.Manager, MemberPeriod.Open(now.AddDays(-10)), now, by: new UserId(Owner));
                context.Documents.Add(document);
            }

            await context.SaveChangesAsync();
        }

        _scope = _services.CreateAsyncScope();
        _checks = _scope.ServiceProvider.GetRequiredService<AccessChecks<IFilingRequest>>();
    }
}
