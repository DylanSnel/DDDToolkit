using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The questions for several keys at once, and for a part of the tree, over a database: each is one statement, in
/// Tenancy's own context and in a module's, composes into the module's own query, and answers the same on SQLite
/// and on Postgres.
/// <para>
/// Harbor: North and South under the root, Coast under North. Grace supervises North; Lin operates at Coast.
/// A widget hangs at each unit.
/// </para>
/// </summary>
public abstract class KeySetQueryTests(TestDatabases databases) : IAsyncLifetime
{
    private static readonly string[] Keys = [HostCatalogue.WidgetChange, TenancyKeys.UnitsManage, TenancyKeys.RolesManage];

    private TestServices _services = null!;
    private HostTenancy.ProvisionedTenant _harbor = null!;
    private OrganizationUnitId _north;
    private OrganizationUnitId _south;
    private OrganizationUnitId _coast;
    private SeatId _grace;
    private SeatId _lin;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _services = await databases.ServicesAsync();
        _harbor = await _services.ProvisionAsync("harbor");
        _north = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "North");
        _south = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "South");
        _coast = await _services.AddUnitAsync(_harbor.Tenant, _north, "Coast", "site");
        _grace = await _services.SeatAtAsync(_harbor, "Grace", _north, HostCatalogue.SupervisorPack);
        _lin = await _services.SeatAtAsync(_harbor, "Lin", _coast, HostCatalogue.OperatorPack);

        await _services.BySystemIn(_harbor.Tenant, async services =>
        {
            var widgets = services.Widgets();
            widgets.Widgets.AddRange(Widget(_harbor.RootUnit, "at Harbor"), Widget(_north, "at North"), Widget(_south, "at South"), Widget(_coast, "at Coast"));
            await widgets.SaveChangesAsync(Cancellation);
        });
    }

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task WhereIHold_answers_every_pair_in_one_statement()
    {
        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            foreach (DbContext context in new DbContext[] { services.Tenancy(), services.Widgets() })
            {
                var held = services.Answers().Over(context).WhereIHold(Keys);

                _services.Commands.Reset();
                (await held.ToListAsync(Cancellation)).Should().BeEquivalentTo(
                    new UnitKey<OrganizationUnitId>[]
                    {
                        new(_north, HostCatalogue.WidgetChange), new(_coast, HostCatalogue.WidgetChange),
                        new(_north, TenancyKeys.UnitsManage), new(_coast, TenancyKeys.UnitsManage),
                    },
                    "two of the three keys, at North and below it, in " + context.GetType().Name);
                _services.Commands.Count.Should().Be(1, "every key is asked about in the one statement");
            }

            // Inside a module's own query it is a part of that query: each widget with a key held where it hangs.
            var widgets = services.Widgets();
            var pairs = services.Answers().Over(widgets).WhereIHold(Keys);
            var listed = from widget in widgets.Widgets
                         join pair in pairs on widget.UnitId equals pair.Unit
                         orderby widget.Name, pair.Key
                         select new { widget.Name, pair.Key };

            _services.Commands.Reset();
            (await listed.ToListAsync(Cancellation)).Select(row => (row.Name, row.Key)).Should().Equal(
                ("at Coast", TenancyKeys.UnitsManage), ("at Coast", HostCatalogue.WidgetChange),
                ("at North", TenancyKeys.UnitsManage), ("at North", HostCatalogue.WidgetChange));
            _services.Commands.Count.Should().Be(1, "the question is a part of the one statement");

            // And as a condition of it: the widgets where one of two keys is held.
            var toChange = widgets.Widgets
                .Where(widget => pairs.Any(pair => pair.Unit == widget.UnitId && pair.Key == HostCatalogue.WidgetChange))
                .Select(widget => widget.Name);
            _services.Commands.Reset();
            (await toChange.ToListAsync(Cancellation)).Should().BeEquivalentTo(["at North", "at Coast"]);
            _services.Commands.Count.Should().Be(1);
        });
    }

    [Fact]
    public async Task RoleKeys_and_UnitsUnder_are_one_statement_each_and_compose()
    {
        await _services.BySeat(_harbor.Tenant, _lin, async services =>
        {
            foreach (DbContext context in new DbContext[] { services.Tenancy(), services.Widgets() })
            {
                var tenancy = services.Answers().Over(context);

                _services.Commands.Reset();
                var roles = await tenancy.RoleKeys([HostCatalogue.WidgetCreate, TenancyKeys.RolesManage]).ToListAsync(Cancellation);
                _services.Commands.Count.Should().Be(1, "in " + context.GetType().Name);
                roles.Should().BeEquivalentTo(
                    new RoleWithKey<RoleId>[]
                    {
                        new(_harbor.AdministratorRole, HostCatalogue.WidgetCreate), new(_harbor.AdministratorRole, TenancyKeys.RolesManage),
                        new(_harbor.RolesByPack[HostCatalogue.SupervisorPack], HostCatalogue.WidgetCreate),
                        new(_harbor.RolesByPack[HostCatalogue.OperatorPack], HostCatalogue.WidgetCreate),
                    });

                _services.Commands.Reset();
                (await tenancy.UnitsUnder(_north).ToListAsync(Cancellation)).Should().BeEquivalentTo([_north, _coast]);
                _services.Commands.Count.Should().Be(1);
            }

            // The widgets that hang under North, in one statement of the module's own.
            var widgets = services.Widgets();
            var under = services.Answers().Over(widgets).UnitsUnder(_north);
            _services.Commands.Reset();
            (await widgets.Widgets.Where(widget => under.Contains(widget.UnitId)).Select(widget => widget.Name).ToListAsync(Cancellation))
                .Should().BeEquivalentTo(["at North", "at Coast"]);
            _services.Commands.Count.Should().Be(1);
        });
    }

    [Fact]
    public async Task The_new_questions_answer_alike_on_both_databases()
    {
        // The same answers on SQLite and on Postgres, for a seat, for system work in the tenant, and for nobody:
        // the test runs on both, and what it expects is one list.
        var other = await _services.ProvisionAsync("orchard");
        OrganizationUnitId[] units = [_harbor.RootUnit, _north, _south, _coast];

        var asLin = await AskAsync(HostCaller.InSeat(_harbor.Tenant, _lin));
        asLin.Held.Should().BeEquivalentTo([(_coast, HostCatalogue.WidgetChange)], "Lin operates at Coast, and holds neither of the other keys anywhere");
        asLin.Under.Should().BeEquivalentTo(units, "the tree is the tenant's to read");
        asLin.UnderOther.Should().BeEmpty("a unit of another tenant answers nothing");
        asLin.Roles.Should().HaveCount(3 + 3, "the administrators hold all three keys, the supervisors two, and the operators one")
            .And.OnlyContain(pair => _harbor.RolesByPack.Values.Contains(pair.Role));

        var bySystem = await AskAsync(HostCaller.SystemIn(_harbor.Tenant));
        bySystem.Held.Should().BeEquivalentTo(units.SelectMany(unit => Keys.Select(key => (unit, key))), "system work holds every key at every unit of its tenant");
        bySystem.Under.Should().BeEquivalentTo(units);
        bySystem.UnderOther.Should().BeEmpty();
        bySystem.Roles.Should().BeEquivalentTo(asLin.Roles, "which roles grant a key does not depend on who asks");

        var asNobody = await AskAsync(HostCaller.Nobody(TenancyRefusals.NotSeated));
        asNobody.Held.Should().BeEmpty();
        asNobody.Under.Should().BeEmpty();
        asNobody.Roles.Should().BeEmpty();

        async Task<(List<(OrganizationUnitId Unit, string Key)> Held, List<OrganizationUnitId> Under, List<OrganizationUnitId> UnderOther, List<(RoleId Role, string Key)> Roles)> AskAsync(HostCaller caller)
            => await _services.RunAsync(caller, async services =>
            {
                var tenancy = services.Answers().Over(services.Widgets());
                return (
                    (await tenancy.WhereIHold(Keys).ToListAsync(Cancellation)).Select(pair => (pair.Unit, pair.Key)).ToList(),
                    await tenancy.UnitsUnder(_harbor.RootUnit).ToListAsync(Cancellation),
                    await tenancy.UnitsUnder(other.RootUnit).ToListAsync(Cancellation),
                    (await tenancy.RoleKeys(Keys).ToListAsync(Cancellation)).Select(pair => (pair.Role, pair.Key)).ToList());
            });
    }

    private Widget Widget(OrganizationUnitId unit, string name) => new() { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, UnitId = unit, Name = name };
}

/// <summary>The questions for several keys, on SQLite in memory.</summary>
public sealed class KeySetQueryTestsOnSqlite() : KeySetQueryTests(TestDatabases.Sqlite);

/// <summary>The questions for several keys, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql.</summary>
public sealed class KeySetQueryTestsOnPostgres(PostgresDatabases postgres) : KeySetQueryTests(postgres);
