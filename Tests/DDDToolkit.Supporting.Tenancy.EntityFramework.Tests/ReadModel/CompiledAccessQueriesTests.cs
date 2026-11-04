namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The questions of the access checks that the Entity Framework read source answers with queries it compiled once per
/// model: a permission for the whole tenant, a permission at a unit, and whether a unit is the tenant's. Each answer is
/// the one the questions work out over the source's rows, over Tenancy's own context and over a module's that maps the
/// read model, and each keeps to the current tenant's rows on every execution.
/// <para>
/// Harbor: North and South under the root, Coast under North. Grace supervises North, Lin operates at Coast, and Wes
/// watches at the root. Bay is a second tenant.
/// </para>
/// </summary>
public abstract class CompiledAccessQueriesTests(TestDatabases databases) : IAsyncLifetime
{
    private TestServices _services = null!;

    private ProvisionedTenant _harbor = null!;
    private ProvisionedTenant _bay = null!;
    private OrganizationUnitId _north;
    private OrganizationUnitId _south;
    private OrganizationUnitId _coast;
    private SeatId _grace;
    private SeatId _lin;
    private SeatId _wes;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _services = await databases.ServicesAsync();

        _harbor = await _services.ProvisionAsync("harbor");
        _bay = await _services.ProvisionAsync("bay");
        _north = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "North");
        _south = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "South");
        _coast = await _services.AddUnitAsync(_harbor.Tenant, _north, "Coast");
        _grace = await _services.SeatAtAsync(_harbor, "Grace", _north, HostCatalogue.SupervisorStarterRole);
        _lin = await _services.SeatAtAsync(_harbor, "Lin", _coast, HostCatalogue.OperatorStarterRole);
        _wes = await _services.SeatAtAsync(_harbor, "Wes", _harbor.RootUnit, HostCatalogue.WatcherStarterRole);
    }

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Each_answer_is_the_one_worked_out_over_the_rows_for_every_seat_unit_and_permission()
    {
        string[] permissions = [HostCatalogue.WidgetRead, HostCatalogue.WidgetChange, TenancyPermissions.SeatsManage, TenancyPermissions.RolesManage];
        OrganizationUnitId[] units = [_harbor.RootUnit, _north, _south, _coast, _bay.RootUnit];
        var answers = new List<bool>();

        async Task CompareAsync(IServiceProvider services)
        {
            foreach (DbContext context in new DbContext[] { services.TenancyContext(), services.Widgets() })
            {
                var compiled = services.Access().In(new EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>(context), EfQueryExecutor.Instance);
                var overTheRows = services.Access().In(new RowsOnly(new EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>(context)), EfQueryExecutor.Instance);
                foreach (var permission in permissions)
                {
                    var atTenant = await compiled.HoldsAtTenantAsync(permission, Cancellation);
                    atTenant.Should().Be(await overTheRows.HoldsAtTenantAsync(permission, Cancellation), permission + " for the whole tenant, in " + context.GetType().Name);
                    answers.Add(atTenant);

                    foreach (var unit in units)
                    {
                        var at = await compiled.HoldsAtAsync(permission, unit, Cancellation);
                        at.Should().Be(await overTheRows.HoldsAtAsync(permission, unit, Cancellation), permission + " at " + unit + ", in " + context.GetType().Name);
                        answers.Add(at);
                    }
                }
            }
        }

        foreach (var seat in new[] { _harbor.AdminSeat, _grace, _lin, _wes })
        {
            await _services.BySeat(_harbor.Tenant, seat, CompareAsync);
        }

        // System work in Harbor holds every permission at every unit of Harbor, and at none of Bay's.
        await _services.BySystemIn(_harbor.Tenant, CompareAsync);

        answers.Should().Contain(true).And.Contain(false, "the seats hold some of the permissions at some of the units");
    }

    [Fact]
    public async Task A_check_reads_one_statement()
    {
        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            var queries = services.TenancyContext().Tenancy();

            _services.Commands.Reset();
            (await queries.HoldsAtAsync(TenancyPermissions.SeatsManage, _coast, Cancellation)).Should().BeTrue("Grace manages seats at North, above Coast");
            (await queries.HoldsAtTenantAsync(TenancyPermissions.SeatsManage, Cancellation)).Should().BeFalse("North is not the root");
            _services.Commands.Count.Should().Be(2, "each question is one statement");
        });
    }

    [Fact]
    public async Task A_compiled_question_keeps_to_the_current_tenant_s_rows_on_every_execution()
    {
        // The same values each time, Harbor's administrator at Harbor's root, asked of the source directly. The tenant filter
        // shows Harbor's rows to a caller in Harbor and hides them from a caller in Bay, whatever the question names.
        var now = DateTimeOffset.UtcNow;
        Task<(bool AtTenant, bool AtRoot, bool HasRoot)> AskInAsync(ProvisionedTenant tenant)
            => _services.BySeat(tenant.Tenant, tenant.AdminSeat, async services =>
            {
                var source = new EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>(services.TenancyContext());
                return (
                    await source.HoldsAtTenantAsync(_harbor.Tenant, _harbor.AdminSeat, TenancyPermissions.SeatsManage, now, Cancellation)!,
                    await source.HoldsAtAsync(_harbor.Tenant, _harbor.AdminSeat, TenancyPermissions.SeatsManage, _harbor.RootUnit, now, Cancellation)!,
                    await source.HasUnitAsync(_harbor.Tenant, _harbor.RootUnit, Cancellation)!);
            });

        (await AskInAsync(_harbor)).Should().Be((true, true, true));
        (await AskInAsync(_bay)).Should().Be((false, false, false), "the filter keeps a caller in Bay to Bay's rows");
        (await AskInAsync(_harbor)).Should().Be((true, true, true), "the filter is read again for every execution");
    }

    /// <summary>A source with the rows of another and no answers of its own, so the questions are worked out over the rows.</summary>
    private sealed class RowsOnly(ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> rows) : ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>
    {
        public IQueryable<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>> SeatRights => rows.SeatRights;

        public IQueryable<OrganizationUnitPath<TenantId, OrganizationUnitId>> UnitPaths => rows.UnitPaths;

        public IQueryable<OrganizationUnitRow<TenantId, OrganizationUnitId>> Units => rows.Units;

        public IQueryable<RoleRow<TenantId, RoleId>> Roles => rows.Roles;

        public IQueryable<PlacementRow<SeatId, OrganizationUnitId>> Placements => rows.Placements;

        public IQueryable<SeatRow<TenantId, SeatId>> Seats => rows.Seats;

        public IQueryable<SeatId>? SeatsHoldingPermissionAt(string permission, OrganizationUnitId unit) => rows.SeatsHoldingPermissionAt(permission, unit);
    }
}

/// <summary>The compiled questions of the access checks, on SQLite in memory.</summary>
public sealed class CompiledAccessQueriesTestsOnSqlite() : CompiledAccessQueriesTests(TestDatabases.Sqlite);

/// <summary>The compiled questions of the access checks, on Postgres, as the tables' owner with no row level security.</summary>
public sealed class CompiledAccessQueriesTestsOnPostgres(PostgresDatabases postgres) : CompiledAccessQueriesTests(postgres);
