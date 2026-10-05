using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The access questions over a database: each is one statement, they compose into a module's own query in the
/// module's own context, grant periods are compared in SQL at the moment asked, and the two-step fallback for
/// modules in other databases gives the same answer.
/// <para>
/// Harbor: North and South under the root, Coast under North. Grace supervises North; Lin operates at Coast.
/// A widget hangs at each unit.
/// </para>
/// </summary>
public abstract class AccessQueryTests(TestDatabases databases) : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private TestServices _services = null!;

    private HostTenancy.ProvisionedTenant _harbor = null!;
    private OrganizationUnitId _north;
    private OrganizationUnitId _south;
    private OrganizationUnitId _coast;
    private SeatId _grace;
    private SeatId _lin;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync(services => services.AddSingleton<TimeProvider>(_clock));

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task UnitsWhereIHold_is_one_statement()
    {
        await BuildHarborAsync();

        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            foreach (DbContext context in new DbContext[] { services.Tenancy(), services.Widgets() })
            {
                var held = services.Answers().Over(context).UnitsWhereIHold(HostCatalogue.WidgetChange);

                _services.Commands.Reset();
                (await held.ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo([_north, _coast], "a key held at North reaches the units below it, in " + context.GetType().Name);
                _services.Commands.Count.Should().Be(1);
            }
        });
    }

    [Fact]
    public async Task Who_holds_a_key_at_a_unit_is_one_statement_and_composes()
    {
        await BuildHarborAsync();
        var hal = await _services.SeatAtAsync(_harbor, "Hal", _south, HostCatalogue.WatcherPack);
        var sue = await _services.SeatAtAsync(_harbor, "Sue", _coast, HostCatalogue.WatcherPack);
        await _services.BySystemIn(_harbor.Tenant, services => services.Seats().SuspendAsync(sue, TestContext.Current.CancellationToken));

        // Grace supervises North, and so reads every grant of Harbor: the administrator at the root, herself at North and
        // Lin at Coast hold the key at Coast. Sue, who is suspended, holds nothing, and Hal holds it at South.
        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            foreach (DbContext context in new DbContext[] { services.Tenancy(), services.Widgets() })
            {
                var tenancy = services.Answers().Over(context);
                var holders = tenancy.SeatsHoldingAt(HostCatalogue.WidgetRead, _coast);

                _services.Commands.Reset();
                (await holders.ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo([_harbor.AdminSeat, _grace, _lin], "asked in " + context.GetType().Name);
                _services.Commands.Count.Should().Be(1);

                // Inside a query of the context's own it is a subquery: the active seats among those that hold the key there.
                var active = tenancy.Seats().Where(seat => holders.Contains(seat.Id) && seat.Status == SeatStatus.Active).Select(seat => seat.Id);
                _services.Commands.Reset();
                (await active.ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo([_harbor.AdminSeat, _grace, _lin]);
                _services.Commands.Count.Should().Be(1, "the question is a subquery of the one statement");

                (await tenancy.SeatsHoldingAt(HostCatalogue.WidgetRead, _south).ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo([_harbor.AdminSeat, hal]);
            }
        });

        // Lin operates widgets at Coast and manages nothing: Lin learns about Lin, in one statement all the same.
        await _services.BySeat(_harbor.Tenant, _lin, async services =>
        {
            var tenancy = services.Answers().Over(services.Widgets());
            _services.Commands.Reset();
            (await tenancy.SeatsHoldingAt(HostCatalogue.WidgetRead, _coast).ToListAsync(TestContext.Current.CancellationToken)).Should().Equal(_lin);
            (await tenancy.SeatsHoldingAt(HostCatalogue.WidgetRead, _south).ToListAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
            _services.Commands.Count.Should().Be(2);
        });

        // System work in the tenant learns every holder.
        var all = await _services.BySystemIn(_harbor.Tenant, services => services.Answers().Over(services.Widgets()).SeatsHoldingAt(HostCatalogue.WidgetRead, _coast).ToListAsync(TestContext.Current.CancellationToken));
        all.Should().BeEquivalentTo([_harbor.AdminSeat, _grace, _lin]);
    }

    [Fact]
    public async Task A_consumer_context_composes_the_questions_into_one_query()
    {
        await BuildHarborAsync();
        var hal = await _services.SeatAtAsync(_harbor, "Hal", _south, HostCatalogue.WatcherPack);
        await _services.BySystemIn(_harbor.Tenant, async services =>
        {
            var widgets = services.Widgets();
            widgets.Widgets.AddRange(
                Widget(_south, "at South, for operators", _harbor.RolesByPack[HostCatalogue.OperatorPack]),
                Widget(_south, "at South, for watchers", _harbor.RolesByPack[HostCatalogue.WatcherPack]),
                Widget(_north, "at North, for operators", _harbor.RolesByPack[HostCatalogue.OperatorPack]));
            await widgets.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await _services.BySeat(_harbor.Tenant, hal, async services =>
        {
            var widgets = services.Widgets();
            var tenancy = services.Answers().Over(widgets);

            // Written as a module writes it, the questions inside its own query: the widgets Hal may change where
            // he holds the key, and those handed to a role that changes widgets in the units he belongs to. Each
            // question is correlated with the widget; one ended with Any() on its own would run by itself first.
            var mine = widgets.Widgets
                .Where(widget => tenancy.UnitsWhereIHold(HostCatalogue.WidgetChange).Contains(widget.UnitId)
                                 || (widget.HandedTo != null
                                     && tenancy.RolesWithKey(HostCatalogue.WidgetChange).Contains(widget.HandedTo.Value)
                                     && tenancy.ReadableUnits().Contains(widget.UnitId)))
                .OrderBy(widget => widget.Name)
                .Select(widget => widget.Name);

            var sql = mine.ToQueryString();
            sql.Should().Contain("\"SeatRights\"").And.Contain("\"OrganizationUnitPaths\"").And.Contain("\"SeatPlacements\"").And.Contain(_services.Database.KeysContain);

            _services.Commands.Reset();
            (await mine.ToListAsync(TestContext.Current.CancellationToken)).Should().Equal("at South, for operators");
            _services.Commands.Count.Should().Be(1, "every question is a subquery of the one statement");
        });
    }

    [Fact]
    public async Task Grant_periods_compare_in_sql_in_a_consumer_context()
    {
        await BuildHarborAsync();
        var hal = await _services.SeatAtAsync(_harbor, "Hal", _north);
        await _services.BySystemIn(_harbor.Tenant, services => services.Seats().PlaceAsync(hal, _south, primary: false, TestContext.Current.CancellationToken));

        // An import: a grant at North that ended yesterday, and one at South that starts tomorrow.
        await _services.GrantAsync(_harbor, hal, _north, HostCatalogue.WatcherPack, until: _clock.Now.AddDays(-1), from: _clock.Now.AddDays(-10));
        await _services.GrantAsync(_harbor, hal, _south, HostCatalogue.WatcherPack, from: _clock.Now.AddDays(1));

        (await ReadableWidgetsAsync(hal)).Should().BeEmpty("neither grant applies now");

        _clock.Advance(TimeSpan.FromDays(2));
        (await ReadableWidgetsAsync(hal)).Should().Equal("at South");

        await _services.BySeat(_harbor.Tenant, hal, services =>
        {
            var sql = services.Answers().Over(services.Widgets()).UnitsWhereIHold(HostCatalogue.WidgetRead).ToQueryString();
            sql.Should().MatchRegex("\"StartsAt\" <= @").And.MatchRegex("\"EndsAt\" IS NULL OR \"?\\w+\"?\\.\"EndsAt\" > @", "the moment is compared in SQL");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task An_expired_grant_answers_nothing_at_read_time_with_no_write()
    {
        await BuildHarborAsync();
        var hal = await _services.SeatAtAsync(_harbor, "Hal", _north);
        await _services.GrantAsync(_harbor, hal, _north, HostCatalogue.SupervisorPack, until: _clock.Now.AddHours(1));
        var stored = await _services.StoredRightsAsync(hal);

        (await HoldsAtAsync(hal, TenancyKeys.UnitsManage, _coast)).Should().BeTrue();

        _clock.Advance(TimeSpan.FromHours(2));
        _services.Commands.Reset();
        (await HoldsAtAsync(hal, TenancyKeys.UnitsManage, _coast)).Should().BeFalse("the grant has ended");

        _services.Commands.Sent.Should().NotContain(command => command.Writes, "the grant expires as it is read; nothing is written when it does");
        (await _services.StoredRightsAsync(hal)).Should().BeEquivalentTo(stored);
    }

    [Fact]
    public async Task HoldsTenantWideAsync_and_HoldsAtAsync_run_through_the_executor()
    {
        await BuildHarborAsync();

        (await AskAsync(_harbor.AdminSeat, questions => questions.HoldsTenantWideAsync(TenancyKeys.RolesManage, TestContext.Current.CancellationToken))).Should().BeTrue();
        (await AskAsync(_grace, questions => questions.HoldsTenantWideAsync(TenancyKeys.UnitsManage, TestContext.Current.CancellationToken))).Should().BeFalse("North is not the root");
        (await AskAsync(_grace, questions => questions.HoldsAtAsync(TenancyKeys.UnitsManage, _coast, TestContext.Current.CancellationToken))).Should().BeTrue();
        (await AskAsync(_grace, questions => questions.HoldsAtAsync(TenancyKeys.UnitsManage, _south, TestContext.Current.CancellationToken))).Should().BeFalse();
        (await AskAsync(_lin, questions => questions.HoldsAtAsync(HostCatalogue.WidgetCreate, _coast, TestContext.Current.CancellationToken))).Should().BeTrue();
    }

    [Fact]
    public async Task RolesWithKey_reads_the_keys_in_sql()
    {
        await BuildHarborAsync();
        var watcher = _harbor.RolesByPack[HostCatalogue.WatcherPack];

        var readers = await _services.BySystemIn(_harbor.Tenant, async services =>
            (await services.Tenancy().Set<HostRole>().ToListAsync(TestContext.Current.CancellationToken)).Where(role => role.Holds(HostCatalogue.WidgetRead)).Select(role => role.Id).ToList());
        readers.Should().HaveCount(4, "the administrators' role holds every key, and the other packs read widgets")
            .And.Contain(watcher);

        await _services.BySeat(_harbor.Tenant, _lin, async services =>
        {
            foreach (DbContext context in new DbContext[] { services.Tenancy(), services.Widgets() })
            {
                var roles = services.Answers().Over(context).RolesWithKey(HostCatalogue.WidgetRead);
                roles.ToQueryString().Should().Contain(_services.Database.KeysContain, "the keys are read in SQL: the JSON on SQLite, the array on Postgres");
                (await roles.ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo(readers);
            }
        });

        await _services.BySystemIn(_harbor.Tenant, services => services.Roles().ArchiveAsync(watcher, TestContext.Current.CancellationToken));
        await _services.BySeat(_harbor.Tenant, _lin, async services =>
            (await services.Answers().Over(services.Widgets()).RolesWithKey(HostCatalogue.WidgetRead).ToListAsync(TestContext.Current.CancellationToken))
                .Should().BeEquivalentTo(readers.Where(role => role != watcher), "an archived role gives no rights"));
    }

    [Fact]
    public async Task KeysIHoldAt_and_ReadableUnits_answer_over_the_database()
    {
        await BuildHarborAsync();

        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            var questions = services.Answers().Over(services.Tenancy());
            var supervisor = await services.Tenancy().Set<HostRole>().SingleAsync(role => role.Id == _harbor.RolesByPack[HostCatalogue.SupervisorPack], TestContext.Current.CancellationToken);

            (await questions.KeysIHoldAt(_coast).ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo(supervisor.Keys, "what Grace holds at North reaches Coast");
            (await questions.KeysIHoldAt(_south).ToListAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
            (await questions.ReadableUnits().ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo([_north, _coast]);
        });

        // System work holds every live key: an answer from the catalogue, in memory, which the executor runs as it is.
        await _services.BySystemIn(_harbor.Tenant, async services =>
        {
            var keys = services.Answers().Over(services.Tenancy()).KeysIHoldAt(_south);
            var catalogue = services.GetRequiredService<TenancyCatalogue>();

            _services.Commands.Reset();
            (await EfQueryExecutor.Instance.ListAsync(keys, TestContext.Current.CancellationToken)).Should().Equal(catalogue.LiveKeys);
            (await EfQueryExecutor.Instance.AnyAsync(keys, TestContext.Current.CancellationToken)).Should().BeTrue();
            (await EfQueryExecutor.Instance.FirstOrDefaultAsync(keys, TestContext.Current.CancellationToken)).Should().Be(catalogue.LiveKeys[0]);
            _services.Commands.Count.Should().Be(0);
        });
    }

    [Fact]
    public async Task The_two_step_fallback_gives_the_same_answer()
    {
        await BuildHarborAsync();

        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            var widgets = services.Widgets();
            var held = services.Answers().Over(widgets).UnitsWhereIHold(HostCatalogue.WidgetChange);
            var inOneStatement = await widgets.Widgets.Where(widget => held.Contains(widget.UnitId)).Select(widget => widget.Name).OrderBy(name => name).ToListAsync(TestContext.Current.CancellationToken);

            // Modules in different databases: ask Tenancy's own context first, then pass the units in.
            _services.Commands.Reset();
            var units = await services.Answers().Over(services.Tenancy()).UnitsWhereIHold(HostCatalogue.WidgetChange).ToListAsync(TestContext.Current.CancellationToken);
            var inTwoSteps = await widgets.Widgets.Where(widget => units.Contains(widget.UnitId)).Select(widget => widget.Name).OrderBy(name => name).ToListAsync(TestContext.Current.CancellationToken);

            inTwoSteps.Should().Equal(inOneStatement).And.Equal("at Coast", "at North");
            _services.Commands.Count.Should().Be(2, "the fallback costs a second round trip");
        });
    }

    private async Task BuildHarborAsync()
    {
        _harbor = await _services.ProvisionAsync("harbor");
        _north = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "North");
        _south = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "South");
        _coast = await _services.AddUnitAsync(_harbor.Tenant, _north, "Coast");
        _grace = await _services.SeatAtAsync(_harbor, "Grace", _north, HostCatalogue.SupervisorPack);
        _lin = await _services.SeatAtAsync(_harbor, "Lin", _coast, HostCatalogue.OperatorPack);

        await _services.BySystemIn(_harbor.Tenant, async services =>
        {
            var widgets = services.Widgets();
            widgets.Widgets.AddRange(
                Widget(_harbor.RootUnit, "at Harbor"),
                Widget(_north, "at North"),
                Widget(_south, "at South"),
                Widget(_coast, "at Coast"));
            await widgets.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
    }

    private Widget Widget(OrganizationUnitId unit, string name, RoleId? handedTo = null)
        => new() { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, UnitId = unit, Name = name, HandedTo = handedTo };

    private Task<List<string>> ReadableWidgetsAsync(SeatId seat)
        => _services.BySeat(_harbor.Tenant, seat, async services =>
        {
            var held = services.Answers().Over(services.Widgets()).UnitsWhereIHold(HostCatalogue.WidgetRead);
            return await services.Widgets().Widgets.Where(widget => held.Contains(widget.UnitId)).Select(widget => widget.Name).ToListAsync(TestContext.Current.CancellationToken);
        });

    private Task<bool> HoldsAtAsync(SeatId seat, string key, OrganizationUnitId unit)
        => AskAsync(seat, questions => questions.HoldsAtAsync(key, unit, TestContext.Current.CancellationToken));

    /// <summary>Asks as <paramref name="seat"/> over the widgets' context, and checks the answer took one statement.</summary>
    private Task<bool> AskAsync(SeatId seat, Func<ITenancyQuestions<TenantId, SeatId, OrganizationUnitId, RoleId>, Task<bool>> ask)
        => _services.BySeat(_harbor.Tenant, seat, async services =>
        {
            var questions = services.Answers().Over(services.Widgets());
            _services.Commands.Reset();
            var answer = await ask(questions);
            _services.Commands.Count.Should().Be(1, "the executor runs the question in the database, once");
            return answer;
        });
}

/// <summary>The access questions over a database, on SQLite in memory.</summary>
public sealed class AccessQueryTestsOnSqlite() : AccessQueryTests(TestDatabases.Sqlite);

/// <summary>The access questions over a database, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class AccessQueryTestsOnPostgres(PostgresDatabases postgres) : AccessQueryTests(postgres);
