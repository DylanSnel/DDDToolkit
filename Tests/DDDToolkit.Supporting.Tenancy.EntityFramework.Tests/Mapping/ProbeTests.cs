using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The six things the Entity Framework mapping stands on, each checked on its own before anything is built on
/// it, on the Entity Framework this build runs and on the oldest one the packages allow, on SQLite and on
/// Postgres: a keyless view over a table the same model owns, a tenant filter that follows the caller from one
/// execution to the next, ids compared with a generic <c>Equals</c> inside the package's generic questions, a
/// role's keys asked through a view, a row the model maps to nothing, read from SQL of the store's own with
/// the application's ids, and a list of the application's ids asked about in one statement.
/// </summary>
public abstract class ProbeTests(TestDatabases databases) : IAsyncLifetime
{
    private TestServices _services = null!;

    /// <summary>The services over the test's own database.</summary>
    protected TestServices Services => _services;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_keyless_view_over_an_owned_table_maps_and_is_absent_from_the_create_script()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");

        await using var scope = _services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();

        var rows = context.Model.FindEntityType(typeof(OrganizationUnitRow<TenantId, OrganizationUnitId>))!;
        rows.FindPrimaryKey().Should().BeNull();
        rows.GetViewName().Should().Be("OrganizationUnits");
        rows.GetTableName().Should().BeNull("a view is not a table the model creates");

        var script = context.Database.GenerateCreateScript();
        Regex.Matches(script, "CREATE TABLE " + Regex.Escape(_services.Database.TenancyTable("OrganizationUnits"))).Should().ContainSingle("the owned units make the table once, and the view adds none");
        script.Should().NotContain("CREATE VIEW");

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var units = await context.Set<OrganizationUnitRow<TenantId, OrganizationUnitId>>().OrderBy(unit => unit.ParentId != null).ToListAsync(TestContext.Current.CancellationToken);

            units.Select(unit => (unit.Id, unit.TenantId, unit.ParentId, unit.Status)).Should().Equal(
                (harbor.RootUnit, harbor.Tenant, (OrganizationUnitId?)null, UnitStatus.Active),
                (north, harbor.Tenant, harbor.RootUnit, UnitStatus.Active));
        }
    }

    [Fact]
    public async Task The_named_tenant_filter_is_evaluated_per_execution()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var orchard = await _services.ProvisionAsync("orchard");

        await using var scope = _services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();
        var units = context.Set<OrganizationUnitRow<TenantId, OrganizationUnitId>>();

        var counts = new List<int>();
        foreach (var tenant in new[] { harbor.Tenant, orchard.Tenant, harbor.Tenant, orchard.Tenant })
        {
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
            {
                counts.Add(await units.CountAsync(TestContext.Current.CancellationToken));
            }
        }

        counts.Should().Equal([2, 1, 2, 1], "the same query, compiled once, reads the tenant of the caller running it");
    }

    [Fact]
    public async Task Generic_Equals_on_converted_struct_ids_translates_to_equals()
    {
        // Rights and paths of a tenant no aggregate here belongs to, written as rows: this proves the query, not
        // how the rows come to be.
        var tenant = new TenantId(42);
        var seat = SeatId.CreateSequential();
        var north = OrganizationUnitId.CreateSequential();
        var northCoast = OrganizationUnitId.CreateSequential();
        var south = OrganizationUnitId.CreateSequential();

        await using var scope = _services.Scope();
        var tenancy = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
        {
            tenancy.AddRange(
                Right(tenant, seat, north, HostCatalogue.WidgetRead),
                Right(tenant, seat, south, HostCatalogue.WidgetCreate),
                Path(tenant, north, north, 0),
                Path(tenant, north, northCoast, 1),
                Path(tenant, south, south, 0));
            await tenancy.SaveChangesAsync(TestContext.Current.CancellationToken);

            widgets.Widgets.AddRange(Widget(tenant, north, "at north"), Widget(tenant, northCoast, "below north"), Widget(tenant, south, "at south"));
            await widgets.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (TenancyCallers.Begin(HostCaller.InSeat(tenant, seat)))
        {
            var answers = scope.ServiceProvider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>();
            var held = answers.Over(widgets).UnitsWhereIHold(HostCatalogue.WidgetRead);
            var readable = widgets.Widgets.Where(widget => held.Contains(widget.UnitId)).OrderBy(widget => widget.Name);

            var sql = readable.ToQueryString();
            sql.Should().MatchRegex("\"SeatId\" = @\\w+", "the generic Equals on a converted id becomes an equality");
            sql.Should().MatchRegex("\"UnitId\" IN \\(\\s*SELECT", "the question is a subquery of the module's own query");

            _services.Commands.Reset();
            (await readable.Select(widget => widget.Name).ToListAsync(TestContext.Current.CancellationToken)).Should().Equal("at north", "below north");
            _services.Commands.Count.Should().Be(1, "one statement answers it");
        }
    }

    [Fact]
    public async Task A_row_the_model_maps_to_nothing_reads_the_applications_ids_from_raw_sql_and_composes()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var south = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "South");
        var coast = await _services.AddUnitAsync(harbor.Tenant, north, "Coast");
        var units = _services.Database.TenancyTable("OrganizationUnits");

        await using var scope = _services.Scope();
        var tenancy = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();

        // What a database function answers the store is read this way: a row that is in the model, so the application's
        // converters turn its columns into ids, and mapped to no table, so only SQL of the store's own reads it. A row
        // made up for one query would get no converter: Entity Framework maps such a row without the conventions the
        // context registers.
        var reach = tenancy.Model.FindEntityType(typeof(MoveReach<OrganizationUnitId>))!;
        reach.FindPrimaryKey().Should().BeNull();
        reach.GetTableName().Should().BeNull();
        reach.GetViewName().Should().BeNull();
        tenancy.Database.GenerateCreateScript().Should().NotContain("MoveReach");

        // The table's name is the model's own, and the unit is a parameter.
        var children =
            "SELECT u.\"Id\" AS \"UnitId\", u.\"Name\" AS \"Key\", NULL AS \"EndsAt\", u.\"ParentId\" AS \"Parent\", u.\"Name\" = 'North' AS \"OfCaller\" FROM "
            + units + " u WHERE u.\"ParentId\" = {0}";
        var below = tenancy.Set<MoveReach<OrganizationUnitId>>().FromSqlRaw(children, harbor.RootUnit.Value);

        (await below.OrderBy(row => row.Key).ToListAsync(TestContext.Current.CancellationToken)).Should().Equal(
            new MoveReach<OrganizationUnitId>(north, "North", null, harbor.RootUnit, true),
            new MoveReach<OrganizationUnitId>(south, "South", null, harbor.RootUnit, false));

        // And it composes: the raw query is a subquery of one over the model's own rows, in one statement.
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var parents = below.Select(row => row.UnitId);
            var under = tenancy.Set<OrganizationUnitRow<TenantId, OrganizationUnitId>>()
                .Where(unit => unit.ParentId != null && parents.Contains(unit.ParentId.Value))
                .Select(unit => unit.Id);

            _services.Commands.Reset();
            (await under.ToListAsync(TestContext.Current.CancellationToken)).Should().Equal(coast);
            _services.Commands.Count.Should().Be(1, "one statement answers it");
        }
    }

    [Fact]
    public async Task A_list_of_the_applications_ids_is_asked_about_in_one_statement()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var grace = await _services.AddSeatAsync(harbor.Tenant, Guid.NewGuid(), "Grace");
        var lin = await _services.AddSeatAsync(harbor.Tenant, Guid.NewGuid(), "Lin");
        await _services.AddSeatAsync(harbor.Tenant, Guid.NewGuid(), "Hal");

        await using var scope = _services.Scope();
        var tenancy = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();

        // What the store does for the directory's names by id: the ids are the application's own structs, which
        // the context converts, in a list the query asks about with Contains.
        IReadOnlyCollection<SeatId> only = [lin, SeatId.CreateSequential(), grace];
        var asked = only.ToArray();
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var named = tenancy.Set<HostSeat>().AsNoTracking().Where(seat => asked.Contains(seat.Id)).OrderBy(seat => seat.DisplayName).Select(seat => seat.DisplayName);

            _services.Commands.Reset();
            (await named.ToListAsync(TestContext.Current.CancellationToken)).Should().Equal("Grace", "Lin");
            _services.Commands.Count.Should().Be(1, "the list travels with the one statement, and the database picks the seats");
            _services.Commands.Commands.Single().Should().NotContain(SeatIdentityColumn, "only what is selected is read");
        }
    }

    private const string SeatIdentityColumn = "\"Identity\"";

    /// <summary>
    /// Asks which roles hold a key, through the role view of the widgets' context and in memory over the roles
    /// themselves, and checks the SQL it became with <paramref name="translated"/>.
    /// </summary>
    protected async Task KeysContainAsync(Action<string> translated)
    {
        var harbor = await _services.ProvisionAsync("harbor");

        await using var scope = _services.Scope();
        var tenancy = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var expected = (await tenancy.Set<HostRole>().ToListAsync(TestContext.Current.CancellationToken))
                .Where(role => role.Keys.Contains(HostCatalogue.WidgetRead))
                .Select(role => role.Id)
                .ToList();
            expected.Should().HaveCountGreaterThan(1, "several packs hold the key, so the question has something to tell apart");

            var holders = widgets.Set<RoleRow<TenantId, RoleId>>().Where(role => role.Keys.Contains(HostCatalogue.WidgetRead)).Select(role => role.Id);
            translated(holders.ToQueryString());

            (await holders.ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo(expected);
        }
    }

    private static SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId> Right(TenantId tenant, SeatId seat, OrganizationUnitId unit, string key)
        => new()
        {
            TenantId = tenant,
            SeatId = seat,
            UnitId = unit,
            RoleId = RoleId.CreateSequential(),
            Key = key,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

    private static OrganizationUnitPath<TenantId, OrganizationUnitId> Path(TenantId tenant, OrganizationUnitId ancestor, OrganizationUnitId descendant, int distance)
        => new() { TenantId = tenant, AncestorId = ancestor, DescendantId = descendant, Distance = distance };

    private static Widget Widget(TenantId tenant, OrganizationUnitId unit, string name)
        => new() { Id = Guid.NewGuid(), TenantId = tenant, UnitId = unit, Name = name };
}

/// <summary>The probes on SQLite, in memory, where a role's keys are one JSON document.</summary>
public sealed class ProbeTestsOnSqlite() : ProbeTests(TestDatabases.Sqlite)
{
    [Fact]
    public Task Json_keys_Contains_translates_in_a_keyless_view()
        => KeysContainAsync(sql => sql.Should().Contain("json_each", "SQLite reads the JSON keys in SQL"));
}

/// <summary>
/// The probes on Postgres, as the tables' owner with no row level security: what Npgsql makes of the mapping, where
/// a role's keys are a text array and the ids travel as their stored types.
/// </summary>
public sealed class ProbeTestsOnPostgres(PostgresDatabases postgres) : ProbeTests(postgres)
{
    [Fact]
    public Task Keys_Contains_reads_a_text_array()
        => KeysContainAsync(sql =>
        {
            sql.Should().MatchRegex(@"= ANY \(\w+\.""Keys""\)", "Npgsql asks the array itself, with no JSON in between");
            sql.Should().NotContain("json");
        });

    [Fact]
    public async Task Generic_Equals_on_converted_struct_ids_is_equals_on_npgsql()
    {
        var tenant = new TenantId(42);
        var seat = SeatId.CreateSequential();
        var unit = OrganizationUnitId.CreateSequential();

        await using var scope = Services.Scope();
        var tenancy = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
        {
            tenancy.Add(new SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>
            {
                TenantId = tenant,
                SeatId = seat,
                UnitId = unit,
                RoleId = RoleId.CreateSequential(),
                Key = HostCatalogue.WidgetRead,
                StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            tenancy.Add(new OrganizationUnitPath<TenantId, OrganizationUnitId> { TenantId = tenant, AncestorId = unit, DescendantId = unit, Distance = 0 });
            await tenancy.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (TenancyCallers.Begin(HostCaller.InSeat(tenant, seat)))
        {
            var held = scope.ServiceProvider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>()
                .Over(widgets)
                .UnitsWhereIHold(HostCatalogue.WidgetRead);

            // The seat is a parameter of the column's own type: no cast of the column, which would keep Postgres from
            // the index on it, and no comparison as text.
            var sql = held.ToQueryString();
            sql.Should().MatchRegex(@"\.""SeatId"" = @\w+").And.NotMatchRegex(@"""SeatId""::").And.NotContain("CAST(");
            sql.Should().MatchRegex(@"-- @\w+='" + seat.Value + "'", "the seat travels as its stored Guid");

            (await held.ToListAsync(TestContext.Current.CancellationToken)).Should().Equal(unit);
        }
    }

    [Fact]
    public async Task The_named_tenant_filter_is_evaluated_per_execution_on_npgsql()
    {
        var harbor = await Services.ProvisionAsync("harbor");
        await Services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var orchard = await Services.ProvisionAsync("orchard");

        await using var scope = Services.Scope();
        var units = scope.ServiceProvider.GetRequiredService<TestWidgetContext>().Set<OrganizationUnitRow<TenantId, OrganizationUnitId>>();

        var counts = new List<int>();
        Services.Commands.Reset();
        foreach (var tenant in new[] { harbor.Tenant, orchard.Tenant, harbor.Tenant, orchard.Tenant })
        {
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
            {
                counts.Add(await units.CountAsync(TestContext.Current.CancellationToken));
            }
        }

        counts.Should().Equal([2, 1, 2, 1]);

        // One statement, sent four times: the tenant is a parameter of it, bound anew at each execution.
        Services.Commands.Commands.Distinct().Should().ContainSingle().Which.Should().MatchRegex(@"""TenantId"" = @\w+");
    }
}
