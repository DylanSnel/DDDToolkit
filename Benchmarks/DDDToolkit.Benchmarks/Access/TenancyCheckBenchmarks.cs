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
/// One check of a request that requires a permission of Tenancy's, in a scope that already has its context: the seat
/// begun, and the module's checks asked. Both checks pass. <see cref="AtTenant"/> asks for a permission for the whole
/// tenant, which the tenant's administrator holds at the root. <see cref="AtUnit"/> asks for a permission at a unit,
/// which a supervisor holds there. Each check reads in one statement. The scope and its context are made once, so their
/// cost is not measured. On SQLite in memory a statement costs no round trip; on Postgres in a container, every statement
/// is one.
/// </summary>
[MemoryDiagnoser]
public class TenancyCheckBenchmarks
{
    private readonly StatedRequests.ManageEverySeat _everySeat = new();

    private BenchmarkServer _server = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private AccessChecks<IHostRequest> _checks = null!;
    private ProvisionedTenant _tenant = null!;
    private OrganizationUnitId _unit;
    private SeatId _supervisor;
    private StatedRequests.ManageSeatsAt _seatsAtTheUnit = null!;

    /// <summary>The database the checks read.</summary>
    [Params(BenchmarkServerKind.Sqlite, BenchmarkServerKind.Postgres)]
    public BenchmarkServerKind Server { get; set; }

    /// <summary>
    /// Starts the database with a tenant of 10 units below its root, a supervisor at each, and a container with the
    /// toolkit, the test host's Tenancy and the checks of its requests.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _server = BenchmarkServer.StartAsync(Server).GetAwaiter().GetResult();

        var collection = new ServiceCollection();
        collection.AddDDDToolkitEntityFramework(options => options.UseOutbox<TestTenancyContext>(outbox => outbox
            .AddTenancyDomainEvents<TenantId, SeatId, OrganizationUnitId, RoleId>()
            .AddTenancyInvitationEvents<TenantId, InvitationId, OrganizationUnitId, RoleId, SeatId>()
            .KeepEventLog(log => log.AddTenancyEventLog<TenantId, SeatId, OrganizationUnitId, RoleId>())));
        TestHostTenancy.Add(collection);
        collection.AddDbContext<TestTenancyContext>((services, options) => _server.Use(options).UseDDDToolkit(services));
        _services = collection.BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TestTenancyContext>().Database.EnsureCreated();
        }

        SeedAsync().GetAwaiter().GetResult();
        _seatsAtTheUnit = new StatedRequests.ManageSeatsAt(_unit);

        _scope = _services.CreateAsyncScope();
        _checks = _scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();
    }

    /// <summary>Disposes the scope and the container, and stops the database.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _services.Dispose();
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>The administrator manages every seat of the tenant.</summary>
    [Benchmark]
    public Task AtTenant() => RequireAsync(TenancyUser<TenantId, SeatId>.InSeat(_tenant.Tenant, _tenant.AdminSeat), _everySeat);

    /// <summary>A supervisor manages the seats at its own unit.</summary>
    [Benchmark]
    public Task AtUnit() => RequireAsync(TenancyUser<TenantId, SeatId>.InSeat(_tenant.Tenant, _supervisor), _seatsAtTheUnit);

    private async Task RequireAsync(TenancyUser<TenantId, SeatId> caller, IHostRequest request)
    {
        using var begun = TenancyUsers.Begin(caller);
        await _checks.RequireAsync(request, CancellationToken.None);
    }

    private async Task SeedAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            _tenant = await scope.ServiceProvider.GetRequiredService<TenantManager>().ProvisionWithIdsAsync(
                scope.ServiceProvider.GetRequiredService<TenancyCatalogue>(),
                new TenantToProvision("harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor", Guid.NewGuid(), ConfigureFirstSeat: seat => seat.Rename("Ada")));
        }

        for (var i = 0; i < 10; i++)
        {
            var unit = await AsSystemAsync(services => services.GetRequiredService<OrganizationManager>().AddUnitAsync(_tenant.RootUnit, "Unit " + i, CancellationToken.None));
            var seat = await AsSystemAsync(services => services.GetRequiredService<SeatManager>().AddAsync(Guid.NewGuid(), CancellationToken.None, configure: added => added.Rename("Supervisor " + i)));
            await AsSystemAsync(services => services.GetRequiredService<SeatManager>().PlaceAsync(seat.Id, unit.Id, primary: true, CancellationToken.None));
            await AsSystemAsync(services => services.GetRequiredService<SeatManager>().GrantAsync(
                seat.Id, _tenant.RolesByStarterRole[HostCatalogue.SupervisorStarterRole], unit.Id, until: null, reason: null, CancellationToken.None));
            (_unit, _supervisor) = (unit.Id, seat.Id);
        }
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
