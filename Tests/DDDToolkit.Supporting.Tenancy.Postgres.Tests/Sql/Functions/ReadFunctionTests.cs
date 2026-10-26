using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// A module asks Tenancy through its read functions, and names no table of Tenancy's: the six rows the access
/// questions read are each answered by a function, which the module's context maps with
/// <c>AddTenancyReadFunctions</c>. A question composed into the module's own query is still one statement, which
/// Postgres plans over Tenancy's tables with their indexes; it answers as it does in Tenancy's own context and as the
/// C# question does over every row; the policies on the tables decide what the functions answer; they answer
/// under names and values of their own, whatever the application stores; and they answer access facts only, never
/// what a seat, a unit or a role is called.
/// </summary>
public abstract class ReadFunctionTests(TenancyPostgres postgres, TenancyNaming names)
{
    /// <summary>The functions, with the columns each answers and their types, as Postgres says them.</summary>
    private static readonly Dictionary<string, string> Answered = new(StringComparer.Ordinal)
    {
        [TenancyFunctionNames.CallerRights] = "TABLE(\"TenantId\" bigint, \"SeatId\" uuid, \"UnitId\" uuid, \"RoleId\" uuid, \"Key\" text, \"StartsAt\" timestamp with time zone, \"EndsAt\" timestamp with time zone)",
        [TenancyFunctionNames.TenantPlacements] = "TABLE(\"SeatId\" uuid, \"UnitId\" uuid, \"IsPrimary\" boolean, \"TenantId\" bigint)",
        [TenancyFunctionNames.TenantRoles] = "TABLE(\"Id\" uuid, \"TenantId\" bigint, \"FromPack\" text, \"Status\" text, \"Keys\" text[])",
        [TenancyFunctionNames.TenantSeats] = "TABLE(\"Id\" uuid, \"TenantId\" bigint, \"Status\" text)",
        [TenancyFunctionNames.TenantUnitPaths] = "TABLE(\"TenantId\" bigint, \"AncestorId\" uuid, \"DescendantId\" uuid, \"Distance\" integer)",
        [TenancyFunctionNames.TenantUnits] = "TABLE(\"Id\" uuid, \"TenantId\" bigint, \"ParentId\" uuid, \"Status\" text)",
    };

    /// <summary>Who asks, signed in with which identity, having picked which tenant.</summary>
    private static readonly Dictionary<string, (Guid Identity, string Slug)> Asking = new(StringComparer.Ordinal)
    {
        ["Ada in harbor"] = (Ada.Identity, "harbor"),
        ["Hiro in harbor"] = (Hiro.Identity, "harbor"),
        ["Seth in harbor"] = (Seth.Identity, "harbor"),
        ["Oli in harbor"] = (Oli.Identity, "harbor"),
        ["Oli in orchard"] = (Oli.Identity, "orchard"),
        ["Sue in harbor, suspended"] = (Sue.Identity, "harbor"),
        ["Eve in harbor, with an ended and a future grant"] = (Eve.Identity, "harbor"),
        ["Odette in orchard"] = (Odette.Identity, "orchard"),
        ["Quin in quay, a suspended tenant"] = (Quin.Identity, "quay"),
        ["Ada in orchard, where she has no seat"] = (Ada.Identity, "orchard"),
        ["A stranger in harbor"] = (Stranger, "harbor"),
    };

    /// <summary>The functions, each with the table of Tenancy's whose rows it answers, by its default name.</summary>
    private static readonly (string Function, string Table)[] Reads =
    [
        (TenancyFunctionNames.CallerRights, "SeatRights"), (TenancyFunctionNames.TenantUnitPaths, "OrganizationUnitPaths"),
        (TenancyFunctionNames.TenantUnits, "OrganizationUnits"), (TenancyFunctionNames.TenantRoles, "Roles"),
        (TenancyFunctionNames.TenantPlacements, "SeatPlacements"), (TenancyFunctionNames.TenantSeats, "Seats"),
    ];

    private static readonly OrganizationUnitId[] EveryUnit = [HarborRoot, North, South, NorthPier, OrchardRoot, QuayRoot];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Seats => [.. Asking.Keys];

    [Fact]
    public async Task A_module_asks_tenancy_through_its_functions_in_one_statement()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder));

        // Seth supervises North, and is placed there and at North Pier below it.
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, async scoped =>
        {
            var widgets = scoped.Widgets();
            var tenancy = scoped.Answers().Over(widgets);

            // Written as a module writes it, the questions inside its own query: the widgets at the units where Seth
            // holds the key to read them and where he belongs, each with the unit its own unit hangs under, how many
            // roles of the tenant let their holders change it, and how many seats the tenant has in use.
            var held = tenancy.UnitsWhereIHold(HostCatalogue.WidgetRead);
            var belongs = tenancy.ReadableUnits();
            var changing = tenancy.RolesWithKey(HostCatalogue.WidgetChange);
            var mine =
                from widget in widgets.Widgets
                where held.Contains(widget.UnitId) && belongs.Contains(widget.UnitId)
                join unit in tenancy.Units() on widget.UnitId equals unit.Id
                orderby widget.Name
                select new
                {
                    widget.Name,
                    Under = unit.ParentId,
                    RolesThatChangeIt = changing.Count(),
                    SeatsInUse = tenancy.Seats().Count(seat => seat.Status == SeatStatus.Active),
                };

            recorder.Clear();
            (await mine.ToListAsync(Cancellation)).Select(widget => (widget.Name, widget.Under, widget.RolesThatChangeIt, widget.SeatsInUse))
                .Should().Equal(("Pump", (OrganizationUnitId?)HarborRoot, 3, 5), ("Valve", (OrganizationUnitId?)North, 3, 5));

            var statement = recorder.Sent.Should().ContainSingle("every question is a subquery of the one statement").Which.Text;
            statement.Should().ContainAll(Answered.Keys.Select(function => "tenancy." + function + "()"), "each row is read from the function that answers it");
            TablesNamedIn(statement).Should().BeEmpty("the module's query names no table of Tenancy's");

            // And each question on its own is one statement.
            (string Question, Func<Task> Ask)[] questions =
            [
                (nameof(tenancy.UnitsWhereIHold), () => tenancy.UnitsWhereIHold(HostCatalogue.WidgetRead).ToListAsync(Cancellation)),
                (nameof(tenancy.ReadableUnits), () => tenancy.ReadableUnits().ToListAsync(Cancellation)),
                (nameof(tenancy.RolesWithKey), () => tenancy.RolesWithKey(HostCatalogue.WidgetRead).ToListAsync(Cancellation)),
                (nameof(tenancy.KeysIHoldAt), () => tenancy.KeysIHoldAt(NorthPier).ToListAsync(Cancellation)),
                (nameof(tenancy.SeatsHoldingAt), () => tenancy.SeatsHoldingAt(HostCatalogue.WidgetRead, NorthPier).ToListAsync(Cancellation)),
                (nameof(tenancy.Units), () => tenancy.Units().ToListAsync(Cancellation)),
                (nameof(tenancy.Roles), () => tenancy.Roles().ToListAsync(Cancellation)),
                (nameof(tenancy.Seats), () => tenancy.Seats().ToListAsync(Cancellation)),
                (nameof(tenancy.HoldsTenantWideAsync), () => tenancy.HoldsTenantWideAsync(HostCatalogue.WidgetRead, Cancellation)),
                (nameof(tenancy.HoldsAtAsync), () => tenancy.HoldsAtAsync(HostCatalogue.WidgetRead, NorthPier, Cancellation)),
            ];

            foreach (var (question, ask) in questions)
            {
                recorder.Clear();
                await ask();
                var asked = recorder.Sent.Should().ContainSingle("{0} is one statement", question).Which.Text;
                asked.Should().Contain("tenancy.");
                TablesNamedIn(asked).Should().BeEmpty("{0} names no table of Tenancy's", question);
            }
        });
    }

    [Fact]
    public async Task A_question_through_the_functions_is_planned_over_tenancys_tables_with_their_indexes()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        await using var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);

        // The seeded tables are a few rows each, which Postgres reads whole whatever indexes they have. With that
        // made the last resort, the plan shows whether an index can serve the question at all.
        await seth.ExecuteAsync("SET LOCAL enable_seqscan = off", Cancellation);

        // The statement Entity Framework sends for a module's query with a question in it, explained as Seth.
        var plan = await services.BySeat(Seth.Identity, Harbor, Seth.Seat, async scoped =>
        {
            var widgets = scoped.Widgets();
            var held = scoped.Answers().Over(widgets).UnitsWhereIHold(HostCatalogue.WidgetRead);
            await using var asked = widgets.Widgets.Where(widget => held.Contains(widget.UnitId)).Select(widget => widget.Name).CreateDbCommand();
            asked.CommandText.Should().Contain("tenancy.caller_rights()").And.Contain("tenancy.tenant_unit_paths()");
            return await seth.PlanAsync(asked, Cancellation);
        });

        var nodes = QueryPlans.Nodes(plan);
        nodes.Should().NotContain(node => node.Type == "Function Scan", "the functions are folded into the module's query, as views would be");
        var (rightsTable, pathsTable) = (names.Of("SeatRights"), names.Of("OrganizationUnitPaths"));
        nodes.Select(node => node.Relation).Should().Contain([names.Of("Widgets"), rightsTable, pathsTable], "the plan reads the tables themselves");
        nodes.Should().NotContain(node => node.Type == "Seq Scan" && (node.Relation == rightsTable || node.Relation == pathsTable), "both are found through an index");

        // The caller's rights for the key are found by the seat and the key, which the question compares as the
        // function answers them and the index holds them. Two indexes of the rights start with the seat, and on a
        // table this small they cost the same; which of them wins on a large one is the scale test's to say.
        var rights = nodes.Where(node => node.Index is not null && (node.IndexCondition ?? string.Empty).Contains($"t.{names.Shown("SeatId")} = ", StringComparison.Ordinal))
            .Should().ContainSingle("the rights are read once, through an index").Subject;
        rights.IndexCondition.Should().Contain($"(t.{names.Shown("Key")})::text = 'widget.read'::text", "the key narrows the index scan, not the rows after it");
        rights.Index.Should().BeOneOf(names.Of("IX_SeatRights_SeatId_Key"), names.Of("PK_SeatRights"));
        nodes.Should().Contain(node => node.Index == names.Of("PK_OrganizationUnitPaths"), "the tree is read by the unit the right is held at");

        // The tables' policies came with them: whose rights these are, and which tenant's tree, is asked of the caller
        // once for the statement.
        QueryPlans.InitPlansCalling(plan, "tenancy.caller_seat(").Should().NotBeEmpty("a seat reads its own rights");
        QueryPlans.InitPlansCalling(plan, "tenancy.caller_tenant(").Should().NotBeEmpty("and its tenant's tree");

        // The question above reads two of the six. Each of them is folded: asked on its own, with what it turns a
        // status or a text into, none is a step of the plan, which reads the function's table in its place.
        foreach (var (function, table) in Reads)
        {
            var alone = QueryPlans.Nodes(await seth.PlanAsync($"SELECT * FROM tenancy.{function}()", Cancellation));
            alone.Should().NotContain(node => node.Type == "Function Scan", "{0} is folded into the query that asks it", function);
            alone.Should().Contain(node => node.Relation == names.Of(table), "{0} is planned as a read of {1}", function, table);
        }
    }

    [Theory]
    [MemberData(nameof(Seats))]
    public async Task Every_question_answers_the_same_through_the_functions_as_through_tenancys_own_context(string who)
    {
        var (identity, slug) = Asking[who];
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var now = await DatabaseNowAsync(database.ConnectionString, Cancellation);
        var keys = TenancyPostgres.Catalogue.LiveKeys;

        // The questions as C# answers them over every row: as the tables' owner, whom no policy holds, with the save's
        // own rights and none of the database's functions.
        await using var unheld = new TenancyServices(
            database,
            configure: collection => collection.AddSingleton<TimeProvider>(new StoppedClock(now)),
            rowLevelSecurity: false,
            databaseKeepsRights: false);
        HostCaller caller;
        List<string> csharp;
        await using (var scope = unheld.Scope())
        {
            caller = await scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(Caller.User(identity), slug, Cancellation);
            using (TenancyCallers.Begin(caller))
            {
                csharp = await AnswersAsync(scope.ServiceProvider, scope.ServiceProvider.Tenancy(), keys);
            }
        }

        // And as that person, under the policies: in a module's context, through the functions, and in Tenancy's own,
        // over its tables.
        await using var services = new TenancyServices(database, configure: collection => collection.AddSingleton<TimeProvider>(new StoppedClock(now)));
        List<string> functions;
        List<string> own;
        using (Callers.Begin(Caller.User(identity)))
        using (TenancyCallers.Begin(caller))
        {
            functions = await services.InScopeAsync(scoped => AnswersAsync(scoped, scoped.Widgets(), keys));
            own = await services.InScopeAsync(scoped => AnswersAsync(scoped, scoped.Tenancy(), keys));
        }

        functions.Should().Equal(own, "a module is answered through the functions what Tenancy's own context reads from its tables");
        functions.Should().Equal(csharp, "and what the questions answer in C# over every row");

        // Answers known beforehand, so that three empty answers do not pass for agreement.
        functions.Single(line => line.StartsWith("Seats: ", StringComparison.Ordinal)).Split(", ").Should().HaveCount(
            caller.Kind != TenancyCallerKind.Seat ? 1 : caller.Tenant == Harbor ? 6 : 2,
            "a seat reads the seats of its tenant, and nobody else any");
        if (who == "Seth in harbor")
        {
            functions.Should().Contain($"UnitsWhereIHold {HostCatalogue.WidgetRead}: {North}, {NorthPier}", "a supervisor at North reads widgets there and below");
            functions.Single(line => line.StartsWith("Rights: ", StringComparison.Ordinal)).Split(", ").Should().HaveCount(6, "the six keys of a supervisor");
        }
    }

    [Fact]
    public async Task The_read_functions_answer_under_the_names_the_modules_model_reads()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);

        // What each function answers, as the catalog has it: the read row's properties, with types of the functions' own.
        (await owner.ListAsync<string>(
                """
                SELECT p.proname || ' ' || pg_catalog.pg_get_function_result(p.oid)
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) AND p.pronargs = 0 ORDER BY 1
                """,
                Cancellation,
                (object)Answered.Keys.ToArray()))
            .Should().Equal(Answered.OrderBy(function => function.Key, StringComparer.Ordinal).Select(function => function.Key + " " + function.Value));

        // And a module's model reads exactly those columns, by those names, whatever its own tables and columns are
        // called: every row it maps to a function, column for column.
        using var widgets = names.WidgetModel();
        var mapped = widgets.Model.GetEntityTypes().Where(entity => entity.GetFunctionName() is not null).ToList();
        mapped.Select(entity => entity.GetFunctionName()).Should().BeEquivalentTo(Answered.Keys);
        foreach (var entity in mapped)
        {
            var function = widgets.Model.FindDbFunction(entity.GetFunctionName()!)!;
            function.Schema.Should().Be(TestTenancyContext.Schema, "{0} is read in Tenancy's schema", function.Name);
            var columns = entity.GetProperties().Select(property => "\"" + property.GetColumnName(StoreObjectIdentifier.DbFunction(function.ModelName)) + "\"");
            columns.Should().BeEquivalentTo(
                Answered[function.Name]["TABLE(".Length..^1].Split(", ").Select(column => column.Split(' ')[0]),
                "{0} answers what {1} reads", function.Name, entity.DisplayName());
        }
    }

    [Fact]
    public async Task The_read_functions_return_enum_names_whatever_is_stored()
    {
        // An application that stores Tenancy's statuses its own way: a seat's as a number, a role's in lower case and
        // a unit's as a letter. The access files are written from its model, so what compares a status compares the
        // stored value, and the functions turn each back into the name of the enum's member.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation, names);
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, names.Sql("ALTER TABLE tenancy.\"Seats\" ALTER COLUMN \"Status\" TYPE integer USING 0"), Cancellation);

        string script;
        using (var stored = new StoredOtherwiseContext(names))
        {
            stored.Model.FindEntityType(typeof(HostSeat))!.FindProperty(nameof(HostSeat.Status))!.GetColumnType().Should().Be("integer");
            script = PostgresRowAccess.Scripts([stored], [], [], new RowAccessExport { Contributions = [new TenancyRowAccessContribution(TenancyPostgres.Catalogue)] }).Single().Script;
        }

        var status = "CASE t.\"" + names.Of("Status") + "\"";
        script.Should().Contain(status + " WHEN 0 THEN 'Active' WHEN 1 THEN 'Suspended' WHEN 2 THEN 'Deactivated' END")
            .And.Contain(status + " WHEN 'active' THEN 'Active' WHEN 'archived' THEN 'Archived' END")
            .And.Contain(status + " WHEN 'A' THEN 'Active' WHEN 'X' THEN 'Archived' END");
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);

        // A tenant's rows, written as that application stores them.
        var tenant = new TenantId(7);
        var (yard, shed) = (Guid.NewGuid(), Guid.NewGuid());
        var (amy, bert, cy) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (keeper, lookout) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            await owner.ExecuteAsync(
                $"""
                INSERT INTO tenancy."Organizations" ("Id", "Version", "Name") VALUES (7, 0, 'Seventh');
                INSERT INTO tenancy."OrganizationUnits" ("Id", "TenantId", "ParentId", "Name", "Status") VALUES
                    ('{yard}', 7, NULL, 'Yard', 'A'), ('{shed}', 7, '{yard}', 'Shed', 'X');
                INSERT INTO tenancy."OrganizationUnitPaths" ("AncestorId", "DescendantId", "TenantId", "Distance") VALUES
                    ('{yard}', '{yard}', 7, 0), ('{shed}', '{shed}', 7, 0), ('{yard}', '{shed}', 7, 1);
                INSERT INTO tenancy."Seats" ("Id", "Version", "TenantId", "Identity", "DisplayName", "Status") VALUES
                    ('{amy}', 0, 7, gen_random_uuid(), 'Amy', 0), ('{bert}', 0, 7, gen_random_uuid(), 'Bert', 1), ('{cy}', 0, 7, gen_random_uuid(), 'Cy', 2);
                INSERT INTO tenancy."Roles" ("Id", "NormalizedName", "Version", "TenantId", "Name", "Description", "FromPack", "Status", "Keys") VALUES
                    ('{keeper}', 'KEEPER', 0, 7, 'Keeper', '', NULL, 'active', ARRAY['widget.read']),
                    ('{lookout}', 'LOOKOUT', 0, 7, 'Lookout', '', NULL, 'archived', ARRAY['widget.read']);
                """,
                Cancellation);
            await owner.CommitAsync(Cancellation);
        }

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>("SELECT \"Id\"::text || ' ' || \"Status\" FROM tenancy.tenant_seats()", Cancellation))
                .Should().BeEquivalentTo($"{amy} Active", $"{bert} Suspended", $"{cy} Deactivated");
            (await owner.ListAsync<string>("SELECT \"Id\"::text || ' ' || \"Status\" FROM tenancy.tenant_roles()", Cancellation))
                .Should().BeEquivalentTo($"{keeper} Active", $"{lookout} Archived");
            (await owner.ListAsync<string>("SELECT \"Id\"::text || ' ' || \"Status\" FROM tenancy.tenant_units()", Cancellation))
                .Should().BeEquivalentTo($"{shed} Archived", $"{yard} Active");
        }

        // A module's model knows nothing of how they are stored: it reads the members' names, and asks by them.
        await using var services = new TenancyServices(database, rowLevelSecurity: false, databaseKeepsRights: false);
        await services.BySystemIn(tenant, async scoped =>
        {
            var widgets = scoped.Widgets();
            var tenancy = scoped.Answers().Over(widgets);

            (await tenancy.Seats().Where(seat => seat.Status == SeatStatus.Suspended).Select(seat => seat.Id).ToListAsync(Cancellation)).Should().Equal(new SeatId(bert));
            (await tenancy.Seats().ToListAsync(Cancellation)).Select(seat => (seat.Id, seat.Status))
                .Should().BeEquivalentTo([(new SeatId(amy), SeatStatus.Active), (new SeatId(bert), SeatStatus.Suspended), (new SeatId(cy), SeatStatus.Deactivated)]);
            (await tenancy.Units().Where(unit => unit.Status == UnitStatus.Archived).Select(unit => unit.Id).ToListAsync(Cancellation)).Should().Equal(new OrganizationUnitId(shed));
            (await tenancy.Roles().Where(role => tenancy.RolesWithKey(HostCatalogue.WidgetRead).Contains(role.Id)).Select(role => role.Id).ToListAsync(Cancellation))
                .Should().Equal([new RoleId(keeper)], "an archived role gives no key, however 'archived' is stored");
        });
    }

    [Fact]
    public async Task The_read_functions_give_system_in_its_whole_tenant_and_a_seat_only_its_own_rights()
    {
        const string SeatsWithRights = "SELECT DISTINCT \"SeatId\" FROM tenancy.caller_rights()";
        const string SeatsAnswered = "SELECT \"Id\"::text || ' in ' || \"TenantId\" FROM tenancy.tenant_seats()";
        static string In(SeatId seat, TenantId tenant) => seat.Value + " in " + tenant.Value;
        string[] harborSeats = [.. new[] { Ada, Eve, Hiro, Oli, Seth, Sue }.Select(person => In(person.Seat, Harbor))];
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // The functions have no condition of their own: the tables' owner, whom no policy holds, is answered every row.
        List<Guid> holdingInHarbor;
        long rightsInHarbor;
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            foreach (var (function, table) in Reads)
            {
                (await owner.ScalarAsync<long>($"SELECT count(*) FROM tenancy.{function}()", Cancellation))
                    .Should().Be(await owner.ScalarAsync<long>($"SELECT count(*) FROM tenancy.\"{table}\"", Cancellation), "{0} answers the rows of {1}", function, table).And.BePositive();
            }

            holdingInHarbor = await owner.ListAsync<Guid>("SELECT DISTINCT \"SeatId\" FROM tenancy.\"SeatRights\" WHERE \"TenantId\" = 1", Cancellation);
            rightsInHarbor = await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"TenantId\" = 1", Cancellation);
            holdingInHarbor.Should().BeEquivalentTo([Ada.Seat.Value, Hiro.Seat.Value, Seth.Seat.Value, Oli.Seat.Value, Eve.Seat.Value]);
        }

        // Oli operates widgets at North Pier and manages nothing. Of the rights he is answered his own three; of the
        // rest, his tenant's, and his own seat in another tenant, which is his to read wherever he signs in.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>(SeatsWithRights, Cancellation)).Should().Equal(Oli.Seat.Value);
            (await oli.ListAsync<string>("SELECT \"Key\" FROM tenancy.caller_rights() ORDER BY 1", Cancellation)).Should().Equal("widget.change", "widget.create", "widget.read");
            (await oli.ListAsync<Guid>("SELECT u.\"Id\" FROM tenancy.tenant_units() u", Cancellation)).Should().BeEquivalentTo([HarborRoot.Value, North.Value, NorthPier.Value, South.Value]);
            (await oli.ListAsync<long>("SELECT DISTINCT \"TenantId\" FROM tenancy.tenant_unit_paths()", Cancellation)).Should().Equal(Harbor.Value);
            (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.tenant_unit_paths()", Cancellation)).Should().Be(8, "each unit with itself, three below the root, and North Pier below North");
            (await oli.ListAsync<Guid>("SELECT r.\"Id\" FROM tenancy.tenant_roles() r", Cancellation)).Should().BeEquivalentTo(
                [HarborRoles.Administrator.Value, GrantsDesk.Value, HarborRoles.Operator.Value, HarborRoles.Supervisor.Value, HarborRoles.Watcher.Value]);
            (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.tenant_placements()", Cancellation)).Should().Be(7, "Seth is placed twice");
            (await oli.ListAsync<long>("SELECT DISTINCT \"TenantId\" FROM tenancy.tenant_placements()", Cancellation)).Should().Equal(Harbor.Value);
            (await oli.ListAsync<string>(SeatsAnswered, Cancellation)).Should().BeEquivalentTo([.. harborSeats, In(OliInOrchard, Orchard)]);
        }

        // Ada administers Harbor, and is answered her own rights all the same: every live key, at the root.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ListAsync<Guid>(SeatsWithRights, Cancellation)).Should().Equal(Ada.Seat.Value);
            (await ada.ScalarAsync<long>("SELECT count(*) FROM tenancy.caller_rights()", Cancellation)).Should().Be(TenancyPostgres.Catalogue.LiveKeys.Count);
            (await ada.ListAsync<string>(SeatsAnswered, Cancellation)).Should().BeEquivalentTo(harborSeats);
        }

        // In a tenant where she has no seat she is answered nothing of it, and of the seats only her own.
        await using (var elsewhere = await AsCaller.PersonAsync(database, Ada.Identity, Orchard, Cancellation))
        {
            foreach (var function in Answered.Keys.Where(function => function != TenancyFunctionNames.TenantSeats))
            {
                (await elsewhere.ScalarAsync<long>($"SELECT count(*) FROM tenancy.{function}()", Cancellation)).Should().Be(0, "{0} answers a person without a seat there nothing", function);
            }

            (await elsewhere.ListAsync<string>(SeatsAnswered, Cancellation)).Should().Equal(In(Ada.Seat, Harbor));
        }

        // System work in a tenant, of any module, is answered every right of that tenant and nothing of another.
        foreach (var scope in new[] { TenancyWork.SystemScope, "widgets" })
        {
            await using var system = await AsCaller.SystemInAsync(database, Harbor, scope, Cancellation);
            (await system.ListAsync<Guid>(SeatsWithRights, Cancellation)).Should().BeEquivalentTo(holdingInHarbor);
            (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.caller_rights()", Cancellation)).Should().Be(rightsInHarbor);
            (await system.ListAsync<long>("SELECT DISTINCT \"TenantId\" FROM tenancy.caller_rights()", Cancellation)).Should().Equal(Harbor.Value);
            (await system.ListAsync<string>(SeatsAnswered, Cancellation)).Should().BeEquivalentTo(harborSeats);
            (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.tenant_unit_paths()", Cancellation)).Should().Be(8);
        }

        // And outside any tenant, nothing at all.
        await using (var nowhere = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation))
        {
            foreach (var function in Answered.Keys)
            {
                (await nowhere.ScalarAsync<long>($"SELECT count(*) FROM tenancy.{function}()", Cancellation)).Should().Be(0, "{0} answers system work outside a tenant nothing", function);
            }
        }

        // Through a module's context the rows are kept to the caller's tenant as well: Oli's seat in Orchard, which the
        // database lets him read in Harbor, is not among Harbor's.
        await using var services = new TenancyServices(database);
        (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Widgets().Set<SeatRow<TenantId, SeatId>>()
                .Select(seat => seat.Id).ToListAsync(Cancellation)))
            .Should().BeEquivalentTo([Ada.Seat, Eve.Seat, Hiro.Seat, Oli.Seat, Seth.Seat, Sue.Seat]);
        (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Widgets().Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>()
                .IgnoreQueryFilters().Select(right => right.SeatId).Distinct().ToListAsync(Cancellation)))
            .Should().Equal([Oli.Seat], "with the tenant filter skipped, the database still answers him his own rights alone");
    }

    [Fact]
    public async Task The_read_functions_answer_no_name()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // The tables' owner, whom no policy holds, asks for what a seat, a unit and a role are called: the functions
        // have no such column, for anyone. The tables themselves still have them, for the directory to read.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (string Function, string Column)[] shown =
        [
            (TenancyFunctionNames.TenantSeats, "DisplayName"), (TenancyFunctionNames.TenantSeats, "Identity"),
            (TenancyFunctionNames.TenantUnits, "Name"), (TenancyFunctionNames.TenantUnits, "CostCentre"),
            (TenancyFunctionNames.TenantRoles, "Name"), (TenancyFunctionNames.TenantRoles, "Description"),
        ];

        foreach (var (function, column) in shown)
        {
            var refusal = await FluentActions.Awaiting(() => owner.AttemptAsync($"SELECT f.\"{column}\" FROM tenancy.{function}() f", Cancellation))
                .Should().ThrowAsync<PostgresException>("{0} answers no {1}", function, column);
            refusal.Which.SqlState.Should().Be(PostgresErrorCodes.UndefinedColumn);
        }

        (await owner.ListAsync<string>("SELECT \"DisplayName\" FROM tenancy.\"Seats\" WHERE \"TenantId\" = 3", Cancellation)).Should().Equal("Quin");
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.tenant_seats() WHERE \"TenantId\" = 3", Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_execute_the_read_functions()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        await using (var anonymous = await AsCaller.AnonymousAsync(database, Cancellation))
        {
            foreach (var function in Answered.Keys)
            {
                var refusal = await FluentActions.Awaiting(() => anonymous.AttemptAsync($"SELECT count(*) FROM tenancy.{function}()", Cancellation))
                    .Should().ThrowAsync<PostgresException>(function);
                refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
                refusal.Which.MessageText.Should().Be($"permission denied for function {function}");
            }
        }

        // They are the signed-in user's and system work's in a tenant, and no other role's, every role at once included.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>(
                """
                SELECT p.proname || ' ' || pg_catalog.has_function_privilege('authenticated', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('ddd_system_in', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('anon', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE')
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) ORDER BY 1
                """,
                Cancellation,
                (object)Answered.Keys.ToArray()))
            .Should().Equal(Answered.Keys.Order(StringComparer.Ordinal).Select(function => function + " true true false false"));
    }

    [Fact]
    public async Task A_module_that_maps_tenancys_views_on_postgres_is_refused()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        DbContextOptions<WidgetViews> Options(IServiceProvider application)
            => (DbContextOptions<WidgetViews>)names.Configure(new DbContextOptionsBuilder<WidgetViews>().UseNpgsql(database.ConnectionString)).UseApplicationServiceProvider(application).Options;

        // The host as AddTenancyPostgres() sets it up: the database keeps the rights, and shows a seat only its own.
        await using var services = new TenancyServices(
            database,
            configure: collection => collection.AddPooledDbContextFactory<WidgetViews>((provider, options) => names.Configure(options.UseNpgsql(database.ConnectionString)).UseTenancy(provider)));

        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, async scoped =>
        {
            var answers = scoped.Answers();

            // A module's context that maps the views over Tenancy's tables is refused before it reads anything,
            // whether it is the request's own, made by hand, or taken from a pool.
            await using var views = new WidgetViews(Options(scoped));
            var asking = () => answers.Over(views);
            asking.Should().Throw<InvalidOperationException>().WithMessage(
                "'WidgetViews' maps Tenancy's read model as views over Tenancy's tables*no module reads Tenancy's tables*modelBuilder.AddTenancyReadFunctions<TTenantId, TSeatId, TUnitId, TRoleId>(schema)*");

            await using var pooled = await scoped.GetRequiredService<IDbContextFactory<WidgetViews>>().CreateDbContextAsync(Cancellation);
            FluentActions.Invoking(() => answers.Over(pooled)).Should().Throw<InvalidOperationException>().WithMessage("*AddTenancyReadFunctions*");
            FluentActions.Invoking(() => new EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>(views)).Should().Throw<InvalidOperationException>()
                .WithMessage("*AddTenancyReadFunctions*", "the rows' source refuses it, however the questions are built over it");

            // The module's context that maps the functions is asked, and so is Tenancy's own, whose tables these are.
            (await answers.Over(scoped.Widgets()).UnitsWhereIHold(HostCatalogue.WidgetRead).ToListAsync(Cancellation)).Should().BeEquivalentTo([North, NorthPier]);
            (await answers.Over(scoped.Tenancy()).UnitsWhereIHold(HostCatalogue.WidgetRead).ToListAsync(Cancellation)).Should().BeEquivalentTo([North, NorthPier]);
        });

        using (var functions = names.WidgetModel())
        using (var views = new WidgetViews(Options(services.Provider)))
        {
            TenancyModel.ReadsThroughFunctions(functions.Model).Should().BeTrue();
            TenancyModel.ReadsThroughFunctions(views.Model).Should().BeFalse();
        }

        // Where the database does not keep the rights, as a Postgres without the package or any other database, the
        // views are how a module reads Tenancy, and are asked.
        await using var writing = new TenancyServices(database, rowLevelSecurity: false, databaseKeepsRights: false);
        await writing.BySeat(Seth.Identity, Harbor, Seth.Seat, async scoped =>
        {
            await using var views = new WidgetViews(Options(scoped));
            (await scoped.Answers().Over(views).UnitsWhereIHold(HostCatalogue.WidgetRead).ToListAsync(Cancellation)).Should().BeEquivalentTo([North, NorthPier]);
        });
    }

    /// <summary>
    /// Every question's answer over <paramref name="context"/> for the current caller, one line each, in a fixed
    /// order: the sets as sorted ids, the rows with every column the read model has.
    /// </summary>
    private static async Task<List<string>> AnswersAsync(IServiceProvider scoped, DbContext context, IReadOnlyList<string> keys)
    {
        var tenancy = scoped.Answers().Over(context);
        var lines = new List<string>();
        void Line(string question, IEnumerable<string> answer) => lines.Add(question + ": " + string.Join(", ", answer.Order(StringComparer.Ordinal)));
        async Task IdsAsync<TId>(string question, IQueryable<TId> answer)
            => Line(question, (await EfQueryExecutor.Instance.ListAsync(answer, Cancellation)).Select(id => id!.ToString()!));

        await IdsAsync("ReadableUnits", tenancy.ReadableUnits());
        foreach (var key in keys)
        {
            await IdsAsync($"UnitsWhereIHold {key}", tenancy.UnitsWhereIHold(key));
            await IdsAsync($"RolesWithKey {key}", tenancy.RolesWithKey(key));
            Line($"HoldsTenantWide {key}", [(await tenancy.HoldsTenantWideAsync(key, Cancellation)).ToString()]);

            var heldAt = new List<string>();
            foreach (var unit in EveryUnit)
            {
                heldAt.Add(unit + " " + await tenancy.HoldsAtAsync(key, unit, Cancellation));
            }

            Line($"HoldsAt {key}", heldAt);
        }

        foreach (var unit in EveryUnit)
        {
            await IdsAsync($"KeysIHoldAt {unit}", tenancy.KeysIHoldAt(unit));
            await IdsAsync($"SeatsHoldingAt {HostCatalogue.WidgetRead} {unit}", tenancy.SeatsHoldingAt(HostCatalogue.WidgetRead, unit));
            await IdsAsync($"SeatsHoldingAt {TenancyKeys.GrantsManage} {unit}", tenancy.SeatsHoldingAt(TenancyKeys.GrantsManage, unit));
        }

        Line("Units", (await tenancy.Units().ToListAsync(Cancellation)).Select(unit => $"{unit.Id} of {unit.TenantId} under {unit.ParentId} {unit.Status}"));
        Line("Roles", (await tenancy.Roles().ToListAsync(Cancellation)).Select(role => $"{role.Id} of {role.TenantId} from {role.FromPack} {role.Status} [{string.Join(' ', role.Keys.Order(StringComparer.Ordinal))}]"));
        Line("Seats", (await tenancy.Seats().ToListAsync(Cancellation)).Select(seat => $"{seat.Id} of {seat.TenantId} {seat.Status}"));

        // The rows the questions are worked out from, read as sets: the seat's own rights, which is all of them it
        // reads under the policies, and its tenant's placements and tree.
        var rights = scoped.Answers().Caller.Seat is { } seat
            ? await context.Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().AsNoTracking().Where(right => right.SeatId == seat).ToListAsync(Cancellation)
            : [];
        Line("Rights", rights.Select(right => $"{right.SeatId} at {right.UnitId} by {right.RoleId} '{right.Key}' of {right.TenantId} from {right.StartsAt:O} to {right.EndsAt:O}"));
        Line("Placements", (await context.Set<PlacementRow<SeatId, OrganizationUnitId>>().AsNoTracking().ToListAsync(Cancellation)).Select(placement => $"{placement.SeatId} at {placement.UnitId} {placement.IsPrimary}"));
        Line("Paths", (await context.Set<OrganizationUnitPath<TenantId, OrganizationUnitId>>().AsNoTracking().ToListAsync(Cancellation)).Select(path => $"{path.AncestorId} over {path.DescendantId} of {path.TenantId} by {path.Distance}"));
        return lines;
    }

    /// <summary>The tables of Tenancy's that <paramref name="statement"/> names, as this naming calls them: quoted or not, and never a function that begins like one.</summary>
    private List<string> TablesNamedIn(string statement)
        => [.. Reads.Select(read => names.Of(read.Table)).Where(table => Regex.IsMatch(statement, "tenancy\\.\"?" + Regex.Escape(table) + "\"?(?![\\w(])"))];

    /// <summary>
    /// A module's context that maps Tenancy's read model as views over Tenancy's tables, as a module does on a database
    /// that does not keep the rights: over the tables as its host names them, which a view has to be told.
    /// </summary>
    private sealed class WidgetViews(DbContextOptions<WidgetViews> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(WidgetContext.Schema);
            modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema, TenancyNaming.For(this).Tables);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }

    /// <summary>
    /// Tenancy's context as <see cref="TestTenancyContext"/> maps it, but with the statuses stored as an application
    /// may choose to: a seat's as a number, a role's in lower case and a unit's as a letter. The model the contribution
    /// writes from, which never connects, with its tables and columns named as <paramref name="names"/> names them.
    /// </summary>
    private sealed class StoredOtherwiseContext(TenancyNaming names) : DbContext(names.Configure(new DbContextOptionsBuilder<StoredOtherwiseContext>().UseNpgsql("Host=model-only")).Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(TestTenancyContext.Schema);
            modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(names.Tables, Database);

            // After AddTenancy, so these are what the model says is stored.
            modelBuilder.Entity<HostSeat>().Property(seat => seat.Status).HasConversion<int>();
            modelBuilder.Entity<HostRole>().Property(role => role.Status).HasConversion(new ValueConverter<RoleStatus, string>(
                status => status.ToString().ToLowerInvariant(),
                stored => Enum.Parse<RoleStatus>(stored, true)));
            modelBuilder.Entity<HostOrganization>().OwnsMany(organization => organization.Units, unit => unit.Property(row => row.Status).HasConversion(new ValueConverter<UnitStatus, string>(
                status => status == UnitStatus.Active ? "A" : "X",
                stored => stored == "A" ? UnitStatus.Active : UnitStatus.Archived)));
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }
}

/// <summary>A module reads Tenancy through its functions, under the names Entity Framework gives the tables and columns.</summary>
public sealed class ReadFunctionTestsOnDefaultNames(TenancyPostgres postgres) : ReadFunctionTests(postgres, TenancyNaming.Default);

/// <summary>A module reads Tenancy through its functions, under snake_case names with enums stored as snake_case text.</summary>
public sealed class ReadFunctionTestsOnSnakeCase(TenancyPostgres postgres) : ReadFunctionTests(postgres, TenancyNaming.SnakeCase);
