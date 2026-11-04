using BenchmarkDotNet.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Benchmarks.Infrastructure;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Managers;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using DDDToolkit.Supporting.Tenancy.TestHost.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Benchmarks.Access;

/// <summary>
/// The checks of a request that requires a permission at one unit, and of a request that requires it at two units with a
/// marker on each, in a scope that already has its context. A supervisor holds the permission at a region, and so at
/// the two units below it, so each check passes. Each marker reads in a statement of its own, one after the other. On
/// SQLite in memory a statement costs no round trip; on Postgres in a container, every statement is one.
/// </summary>
[MemoryDiagnoser]
public class TenancyRoundTripBenchmarks
{
    private BenchmarkServer _server = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private AccessChecks<IHostRequest> _checks = null!;
    private ProvisionedTenant _tenant = null!;
    private SeatId _supervisor;
    private StatedRequests.ManageSeatsAt _one = null!;
    private StatedRequests.MoveSeat _two = null!;

    /// <summary>The database the checks read.</summary>
    [Params(BenchmarkServerKind.Sqlite, BenchmarkServerKind.Postgres)]
    public BenchmarkServerKind Server { get; set; }

    /// <summary>Starts the database, provisions a tenant with a region and two units below it, and a supervisor at the region.</summary>
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

    /// <summary>The permission that manages seats, at one unit: one statement.</summary>
    [Benchmark(Baseline = true)]
    public Task OneUnit() => RequireAsync(_one);

    /// <summary>The permission that manages seats, at the unit a seat leaves and at the unit it moves to: two statements.</summary>
    [Benchmark]
    public Task TwoUnits() => RequireAsync(_two);

    private async Task RequireAsync(IHostRequest request)
    {
        using var begun = TenancyUsers.Begin(TenancyUser<TenantId, SeatId>.InSeat(_tenant.Tenant, _supervisor));
        await _checks.RequireAsync(request, CancellationToken.None);
    }

    private async Task SetupAsync()
    {
        _server = await BenchmarkServer.StartAsync(Server);

        var collection = new ServiceCollection();
        collection.AddDDDToolkitEntityFramework(options => options.UseOutbox<TestTenancyContext>(outbox => outbox
            .AddTenancyDomainEvents<TenantId, SeatId, OrganizationUnitId, RoleId>()
            .AddTenancyInvitationEvents<TenantId, InvitationId, OrganizationUnitId, RoleId, SeatId>()
            .KeepEventLog(log => log.AddTenancyEventLog<TenantId, SeatId, OrganizationUnitId, RoleId>())));
        TestHostTenancy.Add(collection);
        collection.AddDbContext<TestTenancyContext>((services, options) => _server.Use(options).UseDDDToolkit(services));
        _services = collection.BuildServiceProvider();

        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestTenancyContext>().Database.EnsureCreatedAsync();
        }

        await using (var scope = _services.CreateAsyncScope())
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            _tenant = await scope.ServiceProvider.GetRequiredService<TenantManager>().ProvisionWithIdsAsync(
                scope.ServiceProvider.GetRequiredService<TenancyCatalogue>(),
                new TenantToProvision("harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor", Guid.NewGuid(), ConfigureFirstSeat: seat => seat.Rename("Ada")));
        }

        var region = await AsSystemAsync(services => services.GetRequiredService<OrganizationManager>().AddUnitAsync(_tenant.RootUnit, "Region", CancellationToken.None));
        var from = await AsSystemAsync(services => services.GetRequiredService<OrganizationManager>().AddUnitAsync(region.Id, "From", CancellationToken.None));
        var to = await AsSystemAsync(services => services.GetRequiredService<OrganizationManager>().AddUnitAsync(region.Id, "To", CancellationToken.None));
        var seat = await AsSystemAsync(services => services.GetRequiredService<SeatManager>().AddAsync(Guid.NewGuid(), CancellationToken.None, configure: added => added.Rename("Supervisor")));
        await AsSystemAsync(services => services.GetRequiredService<SeatManager>().PlaceAsync(seat.Id, region.Id, primary: true, CancellationToken.None));
        await AsSystemAsync(services => services.GetRequiredService<SeatManager>().GrantAsync(
            seat.Id, _tenant.RolesByStarterRole[HostCatalogue.SupervisorStarterRole], region.Id, until: null, reason: null, CancellationToken.None));

        _supervisor = seat.Id;
        _one = new StatedRequests.ManageSeatsAt(from.Id);
        _two = new StatedRequests.MoveSeat(seat.Id, from.Id, to.Id);
        _scope = _services.CreateAsyncScope();
        _checks = _scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();
    }

    private async Task<T> AsSystemAsync<T>(Func<IServiceProvider, Task<T>> act)
    {
        await using var scope = _services.CreateAsyncScope();
        using (TenancyUsers.Begin(TenancyUser<TenantId, SeatId>.SystemIn(_tenant.Tenant)))
        {
            return await act(scope.ServiceProvider);
        }
    }
}
