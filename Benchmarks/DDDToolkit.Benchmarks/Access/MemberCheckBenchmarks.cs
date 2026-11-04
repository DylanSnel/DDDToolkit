using BenchmarkDotNet.Attributes;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.TestHost;
using DDDToolkit.Supporting.Membership.TestHost.Documents;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Benchmarks.Access;

/// <summary>
/// One check of a request with <c>[OnResource(permission)]</c> on a document of the Membership test host, in a scope that
/// already has its context: the caller begun, and the module's checks asked. The caller is a member of the document with
/// the role that gives the permission, so the check passes. The check reads how the caller holds the permission, and
/// until when, in one statement. The scope and its context are made once, so their cost is not measured.
/// </summary>
[MemoryDiagnoser]
public class MemberCheckBenchmarks
{
    private static readonly Guid Owner = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    private static readonly Guid Manager = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    private static readonly DocumentId Minutes = new(Guid.Parse("d0c00000-0000-4000-8000-000000000001"));

    private readonly ShareDocument _request = new(Minutes, new UserId(Guid.Parse("c0c00000-0000-4000-8000-000000000003")));

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private AccessChecks<IFilingRequest> _checks = null!;
    private AccessManager _access = null!;

    /// <summary>
    /// Creates the database with the document and 20 others, each shared with members in roles, and a container with the
    /// toolkit, the test host's Membership and the checks of its requests.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var collection = new ServiceCollection();
        // The seed raises the documents' events, which the benchmark has no use for.
        collection.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, _, _) => Task.CompletedTask));
        FilingHost.Add(collection);
        collection.AddDbContext<FilingContext>((services, options) => options.UseSqlite(_connection).UseDDDToolkit(services));
        _services = collection.BuildServiceProvider();
        _access = _services.GetRequiredService<AccessManager>();

        using var system = _access.BeginAs(CurrentUserSnapshot.System);
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FilingContext>();
        context.Database.EnsureCreated();

        var now = DateTimeOffset.UtcNow;
        var before = now.AddDays(-10);
        var minutes = new Document(Minutes, "Minutes", new UserId(Owner), now.AddDays(-30));
        minutes.ShareWith(new UserId(Manager), DocumentMembership.Manager, MemberPeriod.Open(before), now, by: new UserId(Owner));
        for (var i = 0; i < 8; i++)
        {
            minutes.ShareWith(UserId.Create(), DocumentMembership.Contributor, MemberPeriod.Open(before), now, by: new UserId(Owner));
        }

        context.Documents.Add(minutes);
        for (var i = 0; i < 20; i++)
        {
            var other = new Document(DocumentId.Create(), "Document " + i, UserId.Create(), now.AddDays(-30));
            other.ShareWith(new UserId(Manager), DocumentMembership.Onlooker, MemberPeriod.Open(before), now, by: null);
            context.Documents.Add(other);
        }

        context.SaveChanges();

        _scope = _services.CreateAsyncScope();
        _checks = _scope.ServiceProvider.GetRequiredService<AccessChecks<IFilingRequest>>();
    }

    /// <summary>Disposes the scope and the container, and closes the database.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _services.Dispose();
        _connection.Dispose();
    }

    /// <summary>A member whose role gives the permission that shares the document shares it.</summary>
    [Benchmark]
    public async Task MemberWithARole()
    {
        using var caller = _access.BeginAs(CurrentUserSnapshot.User(Manager));
        await _checks.RequireAsync(_request, CancellationToken.None);
    }
}
