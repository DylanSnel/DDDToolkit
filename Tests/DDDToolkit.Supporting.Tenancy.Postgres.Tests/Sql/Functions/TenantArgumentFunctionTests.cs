using DDDToolkit.Supporting.Tenancy.TestHost;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The questions that take the tenant as an argument, for a policy that runs where the connection names no tenant, as
/// one on the path of a stored file does. They read no setting. Each answers for the seat the caller's verified
/// identity has in the tenant asked about, as the question that reads the setting answers for the calling seat, and
/// only while that seat and the tenant are active: a suspended seat, a suspended or closed tenant, a tenant the person
/// has no seat in and someone without a seat anywhere all learn nothing, the roles of that tenant included.
/// </summary>
public abstract class TenantArgumentFunctionTests(TenancyPostgres postgres, TenancyNaming names)
{
    /// <summary>The five functions, as the catalog names them.</summary>
    private static readonly string[] Functions =
    [
        "tenancy.holds_key_in_tenant(bigint,text)", "tenancy.roles_with_key_in_tenant(bigint,text)", "tenancy.seat_in_tenant(bigint)",
        "tenancy.seated_in_tenant(bigint)", "tenancy.units_where_i_hold_in_tenant(bigint,text)",
    ];

    private static readonly Dictionary<string, (Guid Identity, TenantId Tenant)> Asking = new(StringComparer.Ordinal)
    {
        ["Ada in harbor"] = (Ada.Identity, Harbor),
        ["Hiro in harbor"] = (Hiro.Identity, Harbor),
        ["Seth in harbor"] = (Seth.Identity, Harbor),
        ["Oli in harbor"] = (Oli.Identity, Harbor),
        ["Oli in orchard"] = (Oli.Identity, Orchard),
        ["Sue in harbor, suspended"] = (Sue.Identity, Harbor),
        ["Eve in harbor, with an ended and a future grant"] = (Eve.Identity, Harbor),
        ["Odette in orchard"] = (Odette.Identity, Orchard),
        ["Quin in quay, a suspended tenant"] = (Quin.Identity, Quay),
        ["Ada in orchard, where she has no seat"] = (Ada.Identity, Orchard),
        ["A stranger in harbor"] = (Stranger, Harbor),
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Seats => [.. Asking.Keys];

    [Fact]
    public async Task Seated_in_tenant_is_true_only_while_seat_and_tenant_are_both_active()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Asked with no tenant on the connection, of the tenant in the argument.
        async Task SeatedAsync(Person person, TenantId tenant, SeatId? seat, string because)
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, tenant: null, Cancellation);
            (await caller.ScalarAsync<Guid?>("SELECT tenancy.seat_in_tenant($1)", Cancellation, tenant.Value)).Should().Be(seat?.Value, because);
            (await caller.ScalarAsync<bool>("SELECT tenancy.seated_in_tenant($1)", Cancellation, tenant.Value)).Should().Be(seat is not null, because);
        }

        await SeatedAsync(Ada, Harbor, Ada.Seat, "the seat is active, and so is the tenant");
        await SeatedAsync(Oli, Harbor, Oli.Seat, "the seat the person has in the tenant asked about");
        await SeatedAsync(Oli, Orchard, OliInOrchard, "and the other one in the other");
        await SeatedAsync(Sue, Harbor, null, "the seat is suspended");
        await SeatedAsync(Quin, Quay, null, "an active seat in a suspended tenant");
        await SeatedAsync(Ada, Orchard, null, "a tenant the person has no seat in");
        await SeatedAsync(Ada, new TenantId(404), null, "a tenant there is none of");
        await SeatedAsync(new Person(99, "A stranger"), Harbor, null, "someone without a seat anywhere");

        // As a seat or a tenant stops and starts again, so does the answer.
        await using var services = new TenancyServices(database);
        await services.BySystemIn(Harbor, scoped => scoped.Seats().ReactivateAsync(Sue.Seat, Cancellation));
        await SeatedAsync(Sue, Harbor, Sue.Seat, "the seat is active again");
        await services.BySystemIn(Harbor, scoped => scoped.Seats().DeactivateAsync(Sue.Seat, Cancellation));
        await SeatedAsync(Sue, Harbor, null, "the seat is deactivated");

        await services.BySystemIn(Quay, scoped => scoped.Tenants().ReactivateAsync(Cancellation));
        await SeatedAsync(Quin, Quay, Quin.Seat, "the tenant is active again");
        await services.BySystemIn(Harbor, scoped => scoped.Tenants().SuspendAsync("A pause", Cancellation));
        await SeatedAsync(Ada, Harbor, null, "the tenant is suspended");
        await SeatedAsync(Oli, Orchard, OliInOrchard, "which says nothing of the person's seat in another tenant");
        await services.BySystemIn(Orchard, scoped => scoped.Tenants().CloseAsync("Wound up", Cancellation));
        await SeatedAsync(Oli, Orchard, null, "the tenant is closed");
    }

    [Fact]
    public async Task The_tenant_taking_functions_ignore_the_tenant_setting()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Oli changes widgets in Harbor, at North Pier, and only reads them in Orchard. Whatever tenant the connection
        // names, none, either of his own, or one there is none of, each question answers for the tenant in its argument.
        foreach (var setting in new TenantId?[] { null, Harbor, Orchard, new TenantId(404) })
        {
            await using var oli = await AsCaller.PersonAsync(database, Oli.Identity, setting, Cancellation);
            var because = "the connection names " + (setting?.Value.ToString() ?? "no tenant");

            (await oli.ScalarAsync<Guid?>("SELECT tenancy.seat_in_tenant(1)", Cancellation)).Should().Be(Oli.Seat.Value, because);
            (await oli.ScalarAsync<Guid?>("SELECT tenancy.seat_in_tenant(2)", Cancellation)).Should().Be(OliInOrchard.Value, because);
            (await oli.ScalarAsync<bool>("SELECT tenancy.seated_in_tenant(1) AND tenancy.seated_in_tenant(2)", Cancellation)).Should().BeTrue(because);
            (await oli.ScalarAsync<bool>("SELECT tenancy.holds_key_in_tenant(1, 'widget.change')", Cancellation)).Should().BeTrue(because);
            (await oli.ScalarAsync<bool>("SELECT tenancy.holds_key_in_tenant(2, 'widget.change')", Cancellation)).Should().BeFalse(because);
            (await oli.ScalarAsync<bool>("SELECT tenancy.holds_key_in_tenant(2, 'widget.read')", Cancellation)).Should().BeTrue(because);
            (await oli.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_in_tenant(1, 'widget.change')", Cancellation)).Should().Equal([NorthPier.Value], because);
            (await oli.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_in_tenant(2, 'widget.read')", Cancellation)).Should().Equal([OrchardRoot.Value], because);
            (await oli.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_in_tenant(2, 'widget.change')", Cancellation)).Should().BeEmpty(because);
            (await oli.ListAsync<Guid>("SELECT tenancy.roles_with_key_in_tenant(2, 'tenancy.roles.manage')", Cancellation)).Should().Equal([OrchardRoles.Administrator.Value], because);
            (await oli.ListAsync<Guid>("SELECT tenancy.roles_with_key_in_tenant(1, 'tenancy.roles.manage')", Cancellation)).Should().Equal([HarborRoles.Administrator.Value], because);

            // The questions that read the setting answer for the tenant it names, and for no other.
            (await oli.ScalarAsync<Guid?>("SELECT tenancy.caller_seat()", Cancellation))
                .Should().Be(setting == Harbor ? Oli.Seat.Value : setting == Orchard ? OliInOrchard.Value : null);
        }
    }

    [Theory]
    [MemberData(nameof(Seats))]
    public async Task Units_where_i_hold_in_tenant_answers_as_units_where_i_hold_does_with_the_setting(string who)
    {
        var (identity, tenant) = Asking[who];
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // The same person asks twice: with the tenant on the connection, the questions that read it; with none, the
        // ones that take it. Every one of the five answers as the question it stands in for, for every key.
        await using var set = await AsCaller.PersonAsync(database, identity, tenant, Cancellation);
        await using var given = await AsCaller.PersonAsync(database, identity, tenant: null, Cancellation);

        var seat = await set.ScalarAsync<Guid?>("SELECT tenancy.caller_seat()", Cancellation);
        (await given.ScalarAsync<Guid?>("SELECT tenancy.seat_in_tenant($1)", Cancellation, tenant.Value)).Should().Be(seat, "seat_in_tenant is caller_seat");
        (await given.ScalarAsync<bool>("SELECT tenancy.seated_in_tenant($1)", Cancellation, tenant.Value)).Should().Be(seat is not null, "seated_in_tenant is whether there is one");
        (await given.ScalarAsync<Guid?>("SELECT tenancy.caller_seat()", Cancellation)).Should().BeNull("without the setting, the questions that read it find no seat");

        foreach (var key in TenancyPostgres.Catalogue.LiveKeys)
        {
            (await given.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_in_tenant($1, $2)", Cancellation, tenant.Value, key))
                .Should().BeEquivalentTo(await set.ListAsync<Guid>("SELECT tenancy.units_where_i_hold($1)", Cancellation, key), "units_where_i_hold_in_tenant('{0}') is units_where_i_hold", key);
            (await given.ScalarAsync<bool>("SELECT tenancy.holds_key_in_tenant($1, $2)", Cancellation, tenant.Value, key))
                .Should().Be(await set.ScalarAsync<bool>("SELECT tenancy.holds_key($1)", Cancellation, key), "holds_key_in_tenant('{0}') is holds_key", key);
            (await given.ListAsync<Guid>("SELECT tenancy.roles_with_key_in_tenant($1, $2)", Cancellation, tenant.Value, key))
                .Should().BeEquivalentTo(await set.ListAsync<Guid>("SELECT tenancy.roles_with_key($1)", Cancellation, key), "roles_with_key_in_tenant('{0}') is roles_with_key", key);
        }
    }

    [Fact]
    public async Task Roles_with_key_in_tenant_answers_nothing_to_someone_seatless_in_that_tenant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // The roles of each tenant that let widgets be read, as the tables have them.
        List<Guid> ofHarbor, ofOrchard, ofQuay;
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            const string Reading = "SELECT \"Id\" FROM tenancy.\"Roles\" WHERE \"TenantId\" = $1 AND \"Status\" = 'Active' AND 'widget.read' = ANY (\"Keys\")";
            ofHarbor = await owner.ListAsync<Guid>(Reading, Cancellation, Harbor.Value);
            ofOrchard = await owner.ListAsync<Guid>(Reading, Cancellation, Orchard.Value);
            ofQuay = await owner.ListAsync<Guid>(Reading, Cancellation, Quay.Value);
            ofHarbor.Should().HaveCount(4);
            ofOrchard.Should().HaveCount(4);
            ofQuay.Should().HaveCount(4);
        }

        async Task<List<Guid>> AnsweredAsync(Guid identity, TenantId tenant)
        {
            await using var caller = await AsCaller.PersonAsync(database, identity, tenant: null, Cancellation);
            return await caller.ListAsync<Guid>("SELECT tenancy.roles_with_key_in_tenant($1, 'widget.read')", Cancellation, tenant.Value);
        }

        // A seat of the tenant is answered its roles, whatever it holds itself.
        (await AnsweredAsync(Odette.Identity, Orchard)).Should().BeEquivalentTo(ofOrchard);
        (await AnsweredAsync(Oli.Identity, Orchard)).Should().BeEquivalentTo(ofOrchard);
        (await AnsweredAsync(Eve.Identity, Harbor)).Should().BeEquivalentTo(ofHarbor, "a seat that holds nothing now is a seat");

        // Nobody else learns a role of it: not an administrator of another tenant, not a suspended seat of this one,
        // not a seat of a suspended tenant, and not someone without a seat.
        (await AnsweredAsync(Ada.Identity, Orchard)).Should().BeEmpty("Ada has no seat in Orchard");
        (await AnsweredAsync(Odette.Identity, Harbor)).Should().BeEmpty("Odette has no seat in Harbor");
        (await AnsweredAsync(Sue.Identity, Harbor)).Should().BeEmpty("Sue's seat is suspended");
        (await AnsweredAsync(Quin.Identity, Quay)).Should().BeEmpty("Quay is suspended");
        (await AnsweredAsync(Stranger, Harbor)).Should().BeEmpty("a stranger has no seat");

        // And nothing else of that tenant either.
        foreach (var (identity, tenant) in new[] { (Ada.Identity, Orchard), (Sue.Identity, Harbor), (Quin.Identity, Quay), (Stranger, Harbor) })
        {
            await using var caller = await AsCaller.PersonAsync(database, identity, tenant: null, Cancellation);
            (await caller.ScalarAsync<bool>("SELECT tenancy.seated_in_tenant($1)", Cancellation, tenant.Value)).Should().BeFalse();
            (await caller.ScalarAsync<bool>("SELECT tenancy.seat_in_tenant($1) IS NULL", Cancellation, tenant.Value)).Should().BeTrue();
            foreach (var key in TenancyPostgres.Catalogue.LiveKeys)
            {
                (await caller.ScalarAsync<bool>("SELECT tenancy.holds_key_in_tenant($1, $2)", Cancellation, tenant.Value, key)).Should().BeFalse();
                (await caller.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_in_tenant($1, $2)", Cancellation, tenant.Value, key)).Should().BeEmpty();
                (await caller.ListAsync<Guid>("SELECT tenancy.roles_with_key_in_tenant($1, $2)", Cancellation, tenant.Value, key)).Should().BeEmpty();
            }
        }
    }

    [Fact]
    public async Task Anonymous_callers_cannot_execute_the_tenant_taking_functions()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        (string Function, string Asked)[] questions =
        [
            ("seat_in_tenant", "SELECT tenancy.seat_in_tenant(1)"),
            ("seated_in_tenant", "SELECT tenancy.seated_in_tenant(1)"),
            ("holds_key_in_tenant", "SELECT tenancy.holds_key_in_tenant(1, 'widget.read')"),
            ("units_where_i_hold_in_tenant", "SELECT tenancy.units_where_i_hold_in_tenant(1, 'widget.read')"),
            ("roles_with_key_in_tenant", "SELECT tenancy.roles_with_key_in_tenant(1, 'widget.read')"),
        ];

        // They speak of a signed-in person's own seat: an anonymous caller has none to ask about, and system work in
        // a tenant reads that tenant's rows itself.
        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        await using var systemWork = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation);
        foreach (var (who, caller) in new[] { ("an anonymous caller", anonymous), ("system work", systemWork) })
        {
            foreach (var (function, asked) in questions)
            {
                var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(asked, Cancellation)).Should().ThrowAsync<PostgresException>("{0} asks {1}", who, function);
                refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
                refusal.Which.MessageText.Should().Be($"permission denied for function {function}");
            }
        }

        // As the catalog has it: each runs as its owner, with an empty search path, for signed-in users alone.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>(
                """
                SELECT p.oid::pg_catalog.regprocedure::pg_catalog.text || ' ' || p.prosecdef || ' ' || pg_catalog.array_to_string(p.proconfig, ',')
                       || ' ' || pg_catalog.has_function_privilege('authenticated', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('ddd_system_in', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('anon', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE')
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname LIKE '%\_in\_tenant' ORDER BY p.proname
                """,
                Cancellation))
            .Should().Equal(Functions.Select(function => function + " true search_path=\"\" true false false false"));
    }

    [Fact]
    public async Task A_policy_on_a_path_asks_per_resource_without_the_setting()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Files kept by their path, tenant/unit/name, as a file store keeps them: the policy has the path and the
        // reader's verified identity to go by, and no tenant on the connection. It asks, for each path, where the
        // reader holds the key in the tenant the path names.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"""
            CREATE TABLE widgets.stored_files (path text PRIMARY KEY);
            ALTER TABLE widgets.stored_files ENABLE ROW LEVEL SECURITY;
            GRANT SELECT ON widgets.stored_files TO authenticated, anon;
            CREATE POLICY "Read where the key is held in the tenant of the path" ON widgets.stored_files FOR SELECT TO authenticated
                USING (pg_catalog.split_part(path, '/', 2)::uuid = ANY (ARRAY(
                    SELECT tenancy.units_where_i_hold_in_tenant(pg_catalog.split_part(path, '/', 1)::bigint, 'widget.read'))));
            INSERT INTO widgets.stored_files (path) VALUES
                ('{Harbor.Value}/{North.Value}/pump.pdf'), ('{Harbor.Value}/{NorthPier.Value}/valve.pdf'), ('{Harbor.Value}/{South.Value}/gauge.pdf'),
                ('{Orchard.Value}/{OrchardRoot.Value}/crate.pdf'), ('{Quay.Value}/{QuayRoot.Value}/hull.pdf');
            """,
            Cancellation);

        async Task<List<string>> ReadAsync(Guid identity, TenantId? setting = null)
        {
            await using var reader = await AsCaller.PersonAsync(database, identity, setting, Cancellation);
            return await reader.ListAsync<string>("SELECT pg_catalog.split_part(path, '/', 3) FROM widgets.stored_files ORDER BY 1", Cancellation);
        }

        // With no tenant set: each reads the files at the units where they hold the key, and below, in whichever
        // tenant the path is of. Oli holds it in two, and reads of both in one statement.
        (await ReadAsync(Ada.Identity)).Should().Equal("gauge.pdf", "pump.pdf", "valve.pdf");
        (await ReadAsync(Seth.Identity)).Should().Equal("pump.pdf", "valve.pdf");
        (await ReadAsync(Oli.Identity)).Should().Equal("crate.pdf", "valve.pdf");
        (await ReadAsync(Odette.Identity)).Should().Equal("crate.pdf");

        // The tenant the connection names changes nothing, his own or another.
        (await ReadAsync(Seth.Identity, Orchard)).Should().Equal("pump.pdf", "valve.pdf");
        (await ReadAsync(Oli.Identity, Harbor)).Should().Equal("crate.pdf", "valve.pdf");

        // A suspended seat, a seat of a suspended tenant, a seat whose grants do not apply now and someone
        // without a seat read nothing; neither does anyone who has not signed in.
        (await ReadAsync(Sue.Identity)).Should().BeEmpty("Sue's seat is suspended, though her grant at South stands");
        (await ReadAsync(Quin.Identity)).Should().BeEmpty("Quay is suspended, though Quin administers it");
        (await ReadAsync(Eve.Identity)).Should().BeEmpty("one of Eve's grants has ended and the other has not begun");
        (await ReadAsync(Stranger)).Should().BeEmpty();
        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        (await anonymous.ScalarAsync<long>("SELECT count(*) FROM widgets.stored_files", Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task A_modules_own_function_takes_the_tenant_through_tenancys_declared_question()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // The widgets' own function, declared in C# over Tenancy's question that takes the tenant: the tenant is the
        // function's parameter, so the set of units is worked out once, and the widget is looked up by its key.
        WidgetIsReadableInTenant.RowAccessSql.Should().Be(
            "(({col:TenantId} = {arg:1}) AND ({col:UnitId} = ANY (ARRAY(SELECT {fn:tenancy/units_where_i_hold_in_tenant}({arg:1}, 'widget.read')))))");
        WidgetIsReadableInTenant.RowAccessParameters.Should().Be("bigint");

        var scripts = TenancyPostgres.AccessScripts(functions: [WidgetRules.ReadableInTenant], names: names);
        scripts[1].Should().Contain("CREATE OR REPLACE FUNCTION widgets.readable_in_tenant(uuid, bigint) RETURNS boolean")
            .And.Contain($"WHERE root.\"{names.Of("Id")}\" = $1 AND ((root.\"{names.Of("TenantId")}\" = $2) AND (root.\"{names.Of("UnitId")}\" = ANY (ARRAY(SELECT tenancy.units_where_i_hold_in_tenant($2, 'widget.read')))))");
        foreach (var script in scripts)
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        // Files of widgets, kept by tenant/widget/name: the policy hands the two ids of each path to the function.
        // Signed-in users may ask it by a grant of the test's own: the export grants an access function to the roles
        // whose rules ask it, and no rule asks this one.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"""
            GRANT EXECUTE ON FUNCTION widgets.readable_in_tenant(uuid, bigint) TO authenticated;
            CREATE TABLE widgets.stored_files (path text PRIMARY KEY);
            ALTER TABLE widgets.stored_files ENABLE ROW LEVEL SECURITY;
            GRANT SELECT ON widgets.stored_files TO authenticated;
            CREATE POLICY "Read with the widget it is of" ON widgets.stored_files FOR SELECT TO authenticated
                USING (widgets.readable_in_tenant(pg_catalog.split_part(path, '/', 2)::uuid, pg_catalog.split_part(path, '/', 1)::bigint));
            INSERT INTO widgets.stored_files (path) VALUES
                ('{Harbor.Value}/{Pump.Value}/manual.pdf'), ('{Harbor.Value}/{Valve.Value}/drawing.pdf'), ('{Harbor.Value}/{Gauge.Value}/scale.pdf'),
                ('{Orchard.Value}/{Crate.Value}/label.pdf'), ('{Orchard.Value}/{Pump.Value}/misfiled.pdf');
            """,
            Cancellation);

        async Task<List<string>> ReadAsync(Guid identity)
        {
            await using var reader = await AsCaller.PersonAsync(database, identity, tenant: null, Cancellation);
            return await reader.ListAsync<string>("SELECT pg_catalog.split_part(path, '/', 3) FROM widgets.stored_files ORDER BY 1", Cancellation);
        }

        // With no tenant on the connection. The pump is Harbor's: a path that files it under Orchard reaches nobody,
        // not a reader of Orchard and not a reader of the pump.
        (await ReadAsync(Ada.Identity)).Should().Equal("drawing.pdf", "manual.pdf", "scale.pdf");
        (await ReadAsync(Seth.Identity)).Should().Equal("drawing.pdf", "manual.pdf");
        (await ReadAsync(Oli.Identity)).Should().Equal("drawing.pdf", "label.pdf");
        (await ReadAsync(Odette.Identity)).Should().Equal("label.pdf");
        (await ReadAsync(Sue.Identity)).Should().BeEmpty();
        (await ReadAsync(Quin.Identity)).Should().BeEmpty();
        (await ReadAsync(Stranger)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_other_questions_that_take_the_tenant_are_declared_in_csharp_and_answered_by_the_database()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // The widgets' second function is declared over the four questions the first leaves out: whether the caller
        // is seated in the tenant, whether its seat holds a key there, which seat it is, and the tenant's roles that
        // grant a key. Each is the database's function of the same name, asked with the function's own parameters.
        WidgetIsReadAsInTenant.RowAccessSql.Should()
            .Contain("{fn:tenancy/seated_in_tenant}({arg:1})")
            .And.Contain("{fn:tenancy/holds_key_in_tenant}({arg:1}, 'widget.read')")
            .And.Contain("{fn:tenancy/seat_in_tenant}({arg:1})")
            .And.Contain("{fn:tenancy/roles_with_key_in_tenant}({arg:1}, 'widget.read')")
            .And.Contain("{arg:2}").And.Contain("{arg:3}");
        WidgetIsReadAsInTenant.RowAccessParameters.Should().Be("bigint, uuid, uuid");

        var scripts = TenancyPostgres.AccessScripts(functions: [WidgetRules.ReadAsInTenant], names: names);
        scripts[1].Should().Contain("CREATE OR REPLACE FUNCTION widgets.read_as_in_tenant(uuid, bigint, uuid, uuid) RETURNS boolean")
            .And.Contain("tenancy.seated_in_tenant($2)")
            .And.Contain("tenancy.holds_key_in_tenant($2, 'widget.read')")
            .And.Contain("tenancy.seat_in_tenant($2)")
            .And.Contain("tenancy.roles_with_key_in_tenant($2, 'widget.read')");
        foreach (var script in scripts)
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "GRANT EXECUTE ON FUNCTION widgets.read_as_in_tenant(uuid, bigint, uuid, uuid) TO authenticated;", Cancellation);

        // Asked with no tenant on the connection, as a policy on a stored file or a channel asks.
        async Task<bool> AsksAsync(Guid identity, WidgetId widget, TenantId tenant, SeatId seat, RoleId role)
        {
            await using var caller = await AsCaller.PersonAsync(database, identity, tenant: null, Cancellation);
            return await caller.ScalarAsync<bool>("SELECT coalesce(widgets.read_as_in_tenant($1, $2, $3, $4), false)", Cancellation, widget.Value, tenant.Value, seat.Value, role.Value);
        }

        // Oli operates widgets at North Pier in Harbor, and watches Orchard through another seat.
        (await AsksAsync(Oli.Identity, Valve, Harbor, Oli.Seat, HarborRoles.Operator)).Should().BeTrue();
        (await AsksAsync(Oli.Identity, Crate, Orchard, OliInOrchard, OrchardRoles.Watcher)).Should().BeTrue();

        // Each question decides: another seat than his, a role of the other tenant, a role that grants no such key,
        // and a widget filed under a tenant it is not of.
        (await AsksAsync(Oli.Identity, Valve, Harbor, OliInOrchard, HarborRoles.Operator)).Should().BeFalse("his seat in Harbor is not that one");
        (await AsksAsync(Oli.Identity, Valve, Harbor, Oli.Seat, OrchardRoles.Watcher)).Should().BeFalse("that role is Orchard's");
        (await AsksAsync(Oli.Identity, Valve, Harbor, Oli.Seat, GrantsDesk)).Should().BeFalse("the grants desk gives no widget key");
        (await AsksAsync(Oli.Identity, Valve, Orchard, OliInOrchard, OrchardRoles.Watcher)).Should().BeFalse("the valve is Harbor's");

        // Hiro is seated in Harbor and holds no widget key. Sue's seat is suspended, Quay is, and a stranger has no seat.
        (await AsksAsync(Hiro.Identity, Valve, Harbor, Hiro.Seat, HarborRoles.Operator)).Should().BeFalse();
        (await AsksAsync(Sue.Identity, Gauge, Harbor, Sue.Seat, HarborRoles.Operator)).Should().BeFalse();
        (await AsksAsync(Quin.Identity, Valve, Quay, Quin.Seat, QuayRoles.Administrator)).Should().BeFalse();
        (await AsksAsync(Stranger, Valve, Harbor, Oli.Seat, HarborRoles.Operator)).Should().BeFalse();

        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        await FluentActions.Awaiting(() => anonymous.ScalarAsync<bool>("SELECT widgets.read_as_in_tenant($1, 1, $2, $3)", Cancellation, Valve.Value, Oli.Seat.Value, HarborRoles.Operator.Value))
            .Should().ThrowAsync<PostgresException>();
    }
}

/// <summary>The questions that take the tenant as an argument, under the names Entity Framework gives the tables and columns.</summary>
public sealed class TenantArgumentFunctionTestsOnDefaultNames(TenancyPostgres postgres) : TenantArgumentFunctionTests(postgres, TenancyNaming.Default);

/// <summary>The questions that take the tenant as an argument, under snake_case names with enums stored as snake_case text.</summary>
public sealed class TenantArgumentFunctionTestsOnSnakeCase(TenancyPostgres postgres) : TenantArgumentFunctionTests(postgres, TenancyNaming.SnakeCase);
