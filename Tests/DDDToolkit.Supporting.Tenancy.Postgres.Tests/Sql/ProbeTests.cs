using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// What Tenancy's SQL stands on, checked on Npgsql before anything is built on it, on the Entity Framework and
/// Npgsql this build runs and on the oldest the packages allow: a role's keys are an array SQL asks with
/// <c>= ANY</c>, the periods of grants and rights are instants Postgres compares with its own <c>now()</c>, a
/// key longer than its column is refused by the database, not cut short, a change or a removal that names a row's
/// columns reaches only the rows the caller may read, a trigger that fires after a row is written has written
/// what it writes before the save's next statement runs, a row with no key is read from a function of another
/// schema as it would be from a table, and Postgres plans a query over a function that runs as its caller, with no
/// settings of its own, as a query over the tables the function reads.
/// </summary>
public sealed class ProbeTests(TenancyPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_string_primitive_collection_is_a_text_array_on_npgsql()
    {
        await using (var model = TenancyPostgres.TenancyModel())
        {
            model.Model.FindEntityType(typeof(HostRole))!.FindProperty(nameof(HostRole.Keys))!.GetColumnType().Should().Be("text[]");
        }

        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        await ProvisionHarborAsync(database);

        await using var connection = await OpenAsync(database);
        (await ScalarAsync<string>(connection, """
            SELECT pg_catalog.format_type(a.atttypid, a.atttypmod) FROM pg_catalog.pg_attribute a
            WHERE a.attrelid = 'tenancy."Roles"'::regclass AND a.attname = 'Keys'
            """)).Should().Be("text[]");

        // The keys are stored expanded, so a key a role's keys imply is among them, and = ANY finds it there.
        var catalogue = TenancyPostgres.Catalogue;
        foreach (var key in catalogue.LiveKeys)
        {
            var expected = catalogue.PacksFor(TenantShape.Hierarchical).Count(pack => pack.Keys.Contains(key));
            (await ScalarAsync<long>(connection, "SELECT count(*) FROM tenancy.\"Roles\" WHERE $1 = ANY (\"Keys\")", key))
                .Should().Be(expected, "{0} is held by the roles of the packs that hold it", key);
        }

        (await ScalarAsync<long>(connection, "SELECT count(*) FROM tenancy.\"Roles\" WHERE 'widget.unknown' = ANY (\"Keys\")")).Should().Be(0);

        // And Entity Framework reads the array back as the list the role holds, in its order.
        await using var services = new TenancyServices(database, rowLevelSecurity: false, databaseKeepsRights: false);
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(TenancySeed.Harbor))
        {
            var stored = await services.InScopeAsync(scoped => scoped.Tenancy().Set<HostRole>().ToDictionaryAsync(role => role.FromPack!, role => role.Keys, Cancellation));
            stored.Should().BeEquivalentTo(catalogue.PacksFor(TenantShape.Hierarchical).ToDictionary(pack => pack.Key, pack => pack.Keys), options => options.WithStrictOrdering());
        }
    }

    [Fact]
    public async Task Utc_instants_are_timestamptz_and_compare_with_now_on_npgsql()
    {
        await using (var model = TenancyPostgres.TenancyModel())
        {
            foreach (var entity in model.Model.GetEntityTypes().Where(entity => entity.GetTableName() is "SeatRoleGrants" or "SeatRights" or "SeatPlacements"))
            {
                foreach (var property in entity.GetProperties().Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset)))
                {
                    property.GetColumnType().Should().Be("timestamp with time zone", "{0}.{1} is an instant", entity.GetTableName(), property.Name);
                }
            }
        }

        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        await ProvisionHarborAsync(database);
        var now = await TenancySeed.DatabaseNowAsync(database.ConnectionString, Cancellation);

        // A grant that runs from two days ago to two days ahead, and one that ended yesterday, by the database's clock.
        var (from, until) = (now.AddDays(-2), now.AddDays(2));
        await using var services = new TenancyServices(database, rowLevelSecurity: false, databaseKeepsRights: false);
        await services.BySystemIn(TenancySeed.Harbor, scoped => scoped.Seats().AddSeatAsync(TenancySeed.Oli.Identity, "Oli", Cancellation, TenancySeed.Oli.Seat));
        await services.BySystemIn(TenancySeed.Harbor, scoped => scoped.Seats().PlaceAsync(TenancySeed.Oli.Seat, TenancySeed.HarborRoot, primary: true, Cancellation));
        await services.BySystemIn(TenancySeed.Harbor, scoped => scoped.Seats().GrantAsync(
            TenancySeed.Oli.Seat, TenancySeed.HarborRoot, TenancySeed.HarborRoles.Watcher, until, reason: null, Cancellation, from));
        await services.BySystemIn(TenancySeed.Harbor, scoped => scoped.Seats().GrantAsync(
            TenancySeed.Oli.Seat, TenancySeed.HarborRoot, TenancySeed.HarborRoles.Operator, now.AddDays(-1), reason: null, Cancellation, now.AddDays(-3)));

        await using var connection = await OpenAsync(database);
        await using (var command = new NpgsqlCommand(
            """
            SELECT r."RoleId", r."StartsAt", r."EndsAt", r."StartsAt" <= now() AND (r."EndsAt" IS NULL OR r."EndsAt" > now())
            FROM tenancy."SeatRights" r WHERE r."SeatId" = $1 AND r."Key" = 'widget.read'
            """,
            connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = TenancySeed.Oli.Seat.Value });
            await using var reader = await command.ExecuteReaderAsync(Cancellation);
            var rows = new Dictionary<Guid, (DateTime StartsAt, DateTime EndsAt, bool Live)>();
            while (await reader.ReadAsync(Cancellation))
            {
                rows[reader.GetGuid(0)] = (reader.GetDateTime(1), reader.GetDateTime(2), reader.GetBoolean(3));
            }

            var watching = rows[TenancySeed.HarborRoles.Watcher.Value];
            watching.StartsAt.Kind.Should().Be(DateTimeKind.Utc);
            watching.StartsAt.Should().Be(from.UtcDateTime, "the instant is stored as it was given");
            watching.EndsAt.Should().Be(until.UtcDateTime);
            watching.Live.Should().BeTrue("the database's now() lies between them");
            rows[TenancySeed.HarborRoles.Operator.Value].Live.Should().BeFalse("that grant ended yesterday");
        }

        // Entity Framework reads them back as the same instants, at offset zero.
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(TenancySeed.Harbor))
        {
            var seat = await services.InScopeAsync(scoped => scoped.Tenancy().Set<HostSeat>().SingleAsync(row => row.Id == TenancySeed.Oli.Seat, Cancellation));
            var grant = seat.Placements.Single().Grants.Single(each => each.RoleId == TenancySeed.HarborRoles.Watcher);
            grant.StartsAt.Should().Be(from);
            grant.StartsAt.Offset.Should().Be(TimeSpan.Zero);
            grant.EndsAt.Should().Be(until);
        }
    }

    [Fact]
    public async Task Keys_longer_than_their_columns_fail_on_postgres()
    {
        await using (var model = TenancyPostgres.TenancyModel())
        {
            var rights = model.Model.GetEntityTypes().Single(entity => entity.GetTableName() == "SeatRights");
            rights.FindProperty("Key")!.GetColumnType().Should().Be($"character varying({Permission.MaxKeyLength})");
            model.Model.FindEntityType(typeof(HostRole))!.FindProperty(nameof(HostRole.FromPack))!.GetColumnType().Should().Be($"character varying({RolePack.MaxKeyLength})");
        }

        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        await using var connection = await OpenAsync(database);

        async Task InsertRightAsync(string key)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO tenancy."SeatRights" ("SeatId", "UnitId", "RoleId", "Key", "TenantId", "StartsAt", "EndsAt")
                VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), $1, 1, now(), NULL)
                """,
                connection);
            command.Parameters.Add(new NpgsqlParameter { Value = key });
            await command.ExecuteNonQueryAsync(Cancellation);
        }

        async Task InsertRoleAsync(string pack)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO tenancy."Roles" ("Id", "NormalizedName", "Version", "TenantId", "Name", "Description", "FromPack", "Status", "Keys")
                VALUES (gen_random_uuid(), gen_random_uuid()::text, 1, 1, 'Packed', '', $1, 'Active', ARRAY['widget.read'])
                """,
                connection);
            command.Parameters.Add(new NpgsqlParameter { Value = pack });
            await command.ExecuteNonQueryAsync(Cancellation);
        }

        await InsertRightAsync("widget." + new string('k', Permission.MaxKeyLength - "widget.".Length));
        (await FluentActions.Awaiting(() => InsertRightAsync("widget." + new string('k', Permission.MaxKeyLength + 1 - "widget.".Length)))
                .Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.StringDataRightTruncation, "a key one character too long is refused, not stored cut short");

        await InsertRoleAsync(new string('p', RolePack.MaxKeyLength));
        (await FluentActions.Awaiting(() => InsertRoleAsync(new string('p', RolePack.MaxKeyLength + 1)))
                .Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.StringDataRightTruncation);
    }

    [Fact]
    public async Task An_update_or_delete_naming_columns_of_a_row_the_select_policy_hides_touches_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        var (mine, theirs) = (Guid.NewGuid(), Guid.NewGuid());

        // Slips, each of one holder. A signed-in user reads the slips of the holder its token names, and no policy
        // keeps it from adding, changing or removing any slip.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"""
            CREATE TABLE widgets.slips (id integer PRIMARY KEY, holder uuid NOT NULL, note text NOT NULL);
            INSERT INTO widgets.slips VALUES (1, '{mine}', 'mine'), (2, '{theirs}', 'theirs');
            GRANT SELECT, INSERT, UPDATE, DELETE ON widgets.slips TO authenticated;
            ALTER TABLE widgets.slips ENABLE ROW LEVEL SECURITY;
            CREATE POLICY "Holders read their slips" ON widgets.slips FOR SELECT TO authenticated USING (holder = (SELECT ddd.caller_id()));
            CREATE POLICY "Anyone adds a slip" ON widgets.slips FOR INSERT TO authenticated WITH CHECK (true);
            CREATE POLICY "Anyone changes a slip" ON widgets.slips FOR UPDATE TO authenticated USING (true) WITH CHECK (true);
            CREATE POLICY "Anyone removes a slip" ON widgets.slips FOR DELETE TO authenticated USING (true);
            """,
            Cancellation);

        await using (var caller = await AsCaller.PersonAsync(database, mine, tenant: null, Cancellation))
        {
            (await caller.ListAsync<int>("SELECT id FROM widgets.slips", Cancellation)).Should().Equal(1);

            // A statement that names a column of the row, as every statement Entity Framework writes names the key,
            // needs to read the row, so Postgres holds it to the policy for reading as well: the other holder's slip
            // is not there to change or to remove, and nothing says so but the count.
            (await caller.ExecuteAsync("UPDATE widgets.slips SET note = 'changed' WHERE id = 2", Cancellation)).Should().Be(0);
            (await caller.ExecuteAsync("DELETE FROM widgets.slips WHERE id = 2", Cancellation)).Should().Be(0);

            // It is there for the key, though: the row a save cannot see, it cannot add again either.
            var adding = await FluentActions.Awaiting(() => caller.AttemptAsync("INSERT INTO widgets.slips VALUES (2, $1, 'again')", Cancellation, mine))
                .Should().ThrowAsync<PostgresException>();
            adding.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

            // Its own slip it changes and removes.
            (await caller.ExecuteAsync("UPDATE widgets.slips SET note = 'changed' WHERE id = 1", Cancellation)).Should().Be(1);
            (await caller.ExecuteAsync("DELETE FROM widgets.slips WHERE id = 1", Cancellation)).Should().Be(1);
            await caller.CommitAsync(Cancellation);
        }

        await using var connection = await OpenAsync(database);
        (await ScalarAsync<string>(connection, "SELECT pg_catalog.string_agg(id || ' ' || note, ', ' ORDER BY id) FROM widgets.slips"))
            .Should().Be("2 theirs", "the slip the caller could not read is as it was");
    }

    [Fact]
    public async Task An_after_row_trigger_that_writes_another_table_runs_before_the_next_statement_of_the_save()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);

        // Entries with their lines, and a tally per entry that a trigger opens when the entry is added. A line needs
        // its entry's tally to be there, which the database checks as the line is added, not at commit.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            """
            CREATE SCHEMA probes;
            CREATE TABLE probes.entries ("Id" uuid PRIMARY KEY, "Name" text NOT NULL);
            CREATE TABLE probes.tallies ("EntryId" uuid PRIMARY KEY, "Lines" integer NOT NULL);
            CREATE TABLE probes.lines (
                "Id" uuid PRIMARY KEY,
                "EntryId" uuid NOT NULL REFERENCES probes.entries ("Id"),
                "Text" text NOT NULL,
                CONSTRAINT lines_need_a_tally FOREIGN KEY ("EntryId") REFERENCES probes.tallies ("EntryId"));
            CREATE FUNCTION probes.open_tally() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                INSERT INTO probes.tallies ("EntryId", "Lines") VALUES (NEW."Id", 0);
                RETURN NULL;
            END
            $body$;
            CREATE TRIGGER entries_open_a_tally AFTER INSERT ON probes.entries FOR EACH ROW EXECUTE FUNCTION probes.open_tally();
            """,
            Cancellation);

        var recorder = new CommandRecorder();
        var options = new DbContextOptionsBuilder<Ledger>().UseNpgsql(database.ConnectionString).AddInterceptors(recorder).Options;

        // One save adds an entry and a line of it. Entity Framework writes the entry first, and the line in the
        // statement after it: by then the trigger has opened the tally, or the line would be refused.
        var entry = new LedgerEntry { Id = Guid.NewGuid(), Name = "Rent" };
        await using (var ledger = new Ledger(options))
        {
            ledger.AddRange(entry, new LedgerLine { Id = Guid.NewGuid(), EntryId = entry.Id, Text = "March" });
            (await ledger.SaveChangesAsync(Cancellation)).Should().Be(2);
        }

        var sent = string.Join("\n", recorder.Sent.Select(command => command.Text));
        var entryWritten = sent.IndexOf("INSERT INTO probes.entries", StringComparison.Ordinal);
        entryWritten.Should().BeGreaterThanOrEqualTo(0);
        sent.IndexOf("INSERT INTO probes.lines", StringComparison.Ordinal).Should().BeGreaterThan(entryWritten, "the line follows its entry in the save");

        await using (var connection = await OpenAsync(database))
        {
            (await ScalarAsync<long>(connection, "SELECT count(*) FROM probes.tallies t JOIN probes.lines l ON l.\"EntryId\" = t.\"EntryId\"")).Should().Be(1);
        }

        // The same trigger left until commit writes too late for the statement that follows.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            """
            DROP TRIGGER entries_open_a_tally ON probes.entries;
            CREATE CONSTRAINT TRIGGER entries_open_a_tally AFTER INSERT ON probes.entries
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION probes.open_tally();
            """,
            Cancellation);

        var later = new LedgerEntry { Id = Guid.NewGuid(), Name = "Power" };
        await using var deferred = new Ledger(options);
        deferred.AddRange(later, new LedgerLine { Id = Guid.NewGuid(), EntryId = later.Id, Text = "April" });
        var refused = await FluentActions.Awaiting(() => deferred.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>();
        var failure = refused.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        failure.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        failure.ConstraintName.Should().Be("lines_need_a_tally");
    }

    [Fact]
    public async Task A_keyless_row_maps_to_a_table_function_in_another_schema_on_npgsql()
    {
        // Bins in a depot's schema, which keeps their state as a number and their tags as a document, and a function
        // there that answers the bins as rows under names and types of its own. A counter, in its own schema, keeps
        // picks, each from a bin.
        var (crate, drum, tub) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"""
            CREATE SCHEMA depot;
            CREATE SCHEMA counter;
            CREATE TABLE depot.bins (id uuid PRIMARY KEY, label character varying(40) NOT NULL, state integer NOT NULL, tags jsonb NOT NULL);
            INSERT INTO depot.bins VALUES
                ('{crate}', 'Crate', 0, '["heavy", "wood"]'), ('{drum}', 'Drum', 1, '["heavy"]'), ('{tub}', 'Tub', 0, '[]');
            CREATE FUNCTION depot.bins_on_hand() RETURNS TABLE ("Id" uuid, "Label" text, "State" text, "Tags" text[]) LANGUAGE sql STABLE AS $body$
                SELECT b.id, b.label::pg_catalog.text, CASE b.state WHEN 0 THEN 'Open' WHEN 1 THEN 'Sealed' END,
                       ARRAY(SELECT pg_catalog.jsonb_array_elements_text(b.tags))
                FROM depot.bins b
            $body$;
            CREATE TABLE counter.picks ("Id" uuid PRIMARY KEY, "BinId" uuid NOT NULL, "Count" integer NOT NULL);
            INSERT INTO counter.picks VALUES (gen_random_uuid(), '{crate}', 2), (gen_random_uuid(), '{drum}', 5), (gen_random_uuid(), '{tub}', 7);
            """,
            Cancellation);

        var recorder = new CommandRecorder();
        var options = new DbContextOptionsBuilder<Counter>().UseNpgsql(database.ConnectionString).AddInterceptors(recorder).Options;
        await using var counter = new Counter(options);

        // The model: the row is of the function, in the schema named, and of no table and no view; the counter's own
        // table is all its creation script makes.
        var row = counter.Model.FindEntityType(typeof(BinOnHand))!;
        row.GetFunctionName().Should().Be("bins_on_hand");
        row.GetTableName().Should().BeNull();
        row.GetViewName().Should().BeNull();
        counter.Model.FindDbFunction("bins_on_hand")!.Schema.Should().Be("depot", "the schema given, not the model's own");
        counter.Database.GenerateCreateScript().Should().Contain("counter.picks").And.NotContain("bins");

        // Read as a set: from the function with its schema, under the column names it answers, the state from its
        // name and the tags as a list, and with the row's own filter applied.
        var bins = counter.Set<BinOnHand>().OrderBy(bin => bin.Label);
        bins.ToQueryString().Should().Contain("FROM depot.bins_on_hand() AS").And.Contain(".\"Label\"");
        (await bins.ToListAsync(Cancellation)).Should().BeEquivalentTo(
            [
                new BinOnHand { Id = crate, Label = "Crate", State = BinState.Open, Tags = ["heavy", "wood"] },
                new BinOnHand { Id = tub, Label = "Tub", State = BinState.Open, Tags = [] },
            ],
            options => options.WithStrictOrdering(),
            "the sealed drum is kept out by the filter on the row");
        (await counter.Set<BinOnHand>().IgnoreQueryFilters().Where(bin => bin.State == BinState.Sealed).Select(bin => bin.Label).ToListAsync(Cancellation))
            .Should().Equal("Drum");

        // Inside a query of the counter's own it is a subquery of the one statement, and the tags are asked in SQL.
        recorder.Clear();
        var heavy = counter.Set<BinOnHand>().IgnoreQueryFilters().Where(bin => bin.Tags.Contains("heavy")).Select(bin => bin.Id);
        (await counter.Picks.Where(pick => heavy.Contains(pick.BinId)).OrderBy(pick => pick.Count).Select(pick => pick.Count).ToListAsync(Cancellation))
            .Should().Equal(2, 5);
        recorder.Sent.Should().ContainSingle().Which.Text.Should().Contain("depot.bins_on_hand()").And.Contain("counter.picks");
    }

    [Fact]
    public async Task A_stable_invoker_sql_function_without_settings_is_inlined_on_npgsql()
    {
        // What a holder holds where, a hundred thousand rows of which a signed-in holder reads its own, and which
        // place reaches which. Over them, functions that answer the rows as they are: two written plainly, and the
        // holdings again with a search path of its own, as its owner, as strict, and as volatile. Nothing takes a
        // new function from any role, so the signed-in holder may run each of them.
        var me = Guid.Parse("d0000000-0000-4000-8000-000000000007");
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        const string Held = """RETURNS TABLE ("HolderId" uuid, "PlaceId" uuid, "Key" text) LANGUAGE sql""";
        const string Holdings = """AS $body$ SELECT h."HolderId", h."PlaceId", h."Key"::pg_catalog.text FROM folding.holdings h $body$""";
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            $"""
            CREATE SCHEMA folding;
            CREATE TABLE folding.holdings (
                "HolderId" uuid NOT NULL, "PlaceId" uuid NOT NULL, "Key" character varying(100) NOT NULL,
                PRIMARY KEY ("HolderId", "PlaceId", "Key"));
            CREATE INDEX holdings_of_a_holder_by_key ON folding.holdings ("HolderId", "Key");
            CREATE TABLE folding.reaches ("FromPlaceId" uuid NOT NULL, "PlaceId" uuid NOT NULL, PRIMARY KEY ("FromPlaceId", "PlaceId"));
            INSERT INTO folding.holdings
            SELECT ('d0000000-0000-4000-8000-' || pg_catalog.lpad(holder::text, 12, '0'))::uuid,
                   ('a0000000-0000-4000-8000-' || pg_catalog.lpad(place::text, 12, '0'))::uuid,
                   'key.' || key
            FROM pg_catalog.generate_series(1, 100) holder, pg_catalog.generate_series(1, 100) place, pg_catalog.generate_series(1, 10) key;
            INSERT INTO folding.reaches
            SELECT ('a0000000-0000-4000-8000-' || pg_catalog.lpad(place::text, 12, '0'))::uuid,
                   ('a0000000-0000-4000-8000-' || pg_catalog.lpad((place + step)::text, 12, '0'))::uuid
            FROM pg_catalog.generate_series(1, 100) place, pg_catalog.generate_series(0, 4) step;
            ANALYZE folding.holdings;
            ANALYZE folding.reaches;
            GRANT USAGE ON SCHEMA folding TO authenticated;
            GRANT SELECT ON ALL TABLES IN SCHEMA folding TO authenticated;
            CREATE POLICY "Holders read their own" ON folding.holdings FOR SELECT TO authenticated USING ("HolderId" = (SELECT ddd.caller_id()));
            ALTER TABLE folding.holdings ENABLE ROW LEVEL SECURITY;
            CREATE FUNCTION folding.held() {Held} STABLE {Holdings};
            CREATE FUNCTION folding.held_with_a_path() {Held} STABLE SET search_path = '' {Holdings};
            CREATE FUNCTION folding.held_as_owner() {Held} STABLE SECURITY DEFINER SET search_path = '' {Holdings};
            CREATE FUNCTION folding.held_strict() {Held} STABLE STRICT {Holdings};
            CREATE FUNCTION folding.held_volatile() {Held} VOLATILE {Holdings};
            CREATE FUNCTION folding.reached() RETURNS TABLE ("FromPlaceId" uuid, "PlaceId" uuid) LANGUAGE sql STABLE
                AS $body$ SELECT r."FromPlaceId", r."PlaceId" FROM folding.reaches r $body$;
            """,
            Cancellation);

        // The places reached from where the holder holds a key: the question a module asks, over two functions.
        string Reached(string held)
            => $"""
                SELECT DISTINCT r."PlaceId" FROM folding.{held}() h
                JOIN folding.reached() r ON r."FromPlaceId" = h."PlaceId"
                WHERE h."HolderId" = '{me}' AND h."Key" = 'key.3'
                """;

        await using var caller = await AsCaller.PersonAsync(database, me, tenant: null, Cancellation);

        // Written plainly, the functions are gone from the plan: Postgres reads the tables, the holdings through the
        // index on the holder and the key, with the policy on them applied to the caller.
        var asked = await caller.PlanAsync(Reached("held"), Cancellation);
        var folded = QueryPlans.Nodes(asked);
        folded.Should().NotContain(node => node.Type == "Function Scan", "both functions are folded into the query that asks them");
        folded.Where(node => node.Index is not null && (node.IndexCondition ?? string.Empty).Contains("\"Key\"", StringComparison.Ordinal))
            .Should().ContainSingle("the holdings are found by the holder and the key")
            .Which.Index.Should().Be("holdings_of_a_holder_by_key");
        folded.Should().Contain(node => node.Relation == "reaches");
        QueryPlans.InitPlansCalling(asked, "ddd.caller_id(").Should().NotBeEmpty("the policy on the holdings is still asked who is calling, once for the statement");
        (await caller.ListAsync<Guid>(Reached("held"), Cancellation)).Should().HaveCount(104, "a hundred places held, each reaching itself and the four after it");

        // Any of the four keeps the function a step of its own, which answers every row it may before the query
        // narrows them: no index on the holdings is of use to it.
        foreach (var kept in new[] { "held_with_a_path", "held_as_owner", "held_strict", "held_volatile" })
        {
            var plan = QueryPlans.Nodes(await caller.PlanAsync(Reached(kept), Cancellation));
            plan.Should().ContainSingle(node => node.Type == "Function Scan", "{0} is not folded", kept).Which.Function.Should().Be(kept);
            plan.Should().NotContain(node => node.Relation == "holdings", "the holdings are read inside {0}, out of the planner's sight", kept);
        }

        // Folded or not, the answer is the same, and another holder's rows are not in it: the policy holds the caller
        // to its own holdings through the function too, which only the one that runs as its owner gets past.
        (await caller.ListAsync<Guid>(Reached("held_with_a_path"), Cancellation)).Should().HaveCount(104);
        (await caller.ScalarAsync<long>("SELECT count(*) FROM folding.held()", Cancellation)).Should().Be(1000);
        (await caller.ScalarAsync<long>("SELECT count(*) FROM folding.held_with_a_path()", Cancellation)).Should().Be(1000);
        (await caller.ScalarAsync<long>("SELECT count(*) FROM folding.held_as_owner()", Cancellation)).Should().Be(100_000);
    }

    /// <summary>Provisions Harbor through the use cases, with the seed's fixed ids, on a database without row level security.</summary>
    private static async Task ProvisionHarborAsync(TestDatabase database)
    {
        await using var services = new TenancyServices(database, rowLevelSecurity: false, databaseKeepsRights: false);
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision(
                    "harbor",
                    "Harbor Works",
                    TenantShape.Hierarchical,
                    "Harbor",
                    TenancySeed.Ada.Identity,
                    TenancySeed.Ada.Name,
                    TenantId: TenancySeed.Harbor,
                    RootId: TenancySeed.HarborRoot,
                    AdminSeatId: TenancySeed.Ada.Seat,
                    RoleIds: TenancySeed.HarborRoles.ByPack),
                Cancellation));
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(TestDatabase database)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Cancellation);
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, params object[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return (T)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    /// <summary>A context of the probe's own, over tables the probe makes: entries, and the lines of each.</summary>
    private sealed class Ledger(DbContextOptions<Ledger> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("probes");
            modelBuilder.Entity<LedgerEntry>(entry =>
            {
                entry.ToTable("entries");
                entry.Property(row => row.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<LedgerLine>(line =>
            {
                line.ToTable("lines");
                line.Property(row => row.Id).ValueGeneratedNever();
                line.HasOne<LedgerEntry>().WithMany().HasForeignKey(row => row.EntryId);
            });
        }
    }

    private sealed class LedgerEntry
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class LedgerLine
    {
        public Guid Id { get; set; }

        public Guid EntryId { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// A context of the probe's own, in a schema of its own: its picks, and the bins on hand as a function in another
    /// schema answers them, a row with no key under the column names the function gives.
    /// </summary>
    private sealed class Counter(DbContextOptions<Counter> options) : DbContext(options)
    {
        public DbSet<Pick> Picks => Set<Pick>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("counter");
            modelBuilder.Entity<Pick>(pick =>
            {
                pick.ToTable("picks");
                pick.Property(row => row.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<BinOnHand>(bin =>
            {
                bin.HasNoKey().ToFunction("bins_on_hand", function => function.HasSchema("depot"));
                bin.Property(row => row.Id).HasColumnName("Id");
                bin.Property(row => row.Label).HasColumnName("Label").HasMaxLength(40);
                bin.Property(row => row.State).HasColumnName("State").HasConversion<string>();
                bin.PrimitiveCollection(row => row.Tags).HasColumnName("Tags");
                bin.HasQueryFilter("open", row => row.State == BinState.Open);
            });
        }
    }

    private sealed class Pick
    {
        public Guid Id { get; set; }

        public Guid BinId { get; set; }

        public int Count { get; set; }
    }

    private sealed class BinOnHand
    {
        public Guid Id { get; set; }

        public string Label { get; set; } = string.Empty;

        public BinState State { get; set; }

        public IReadOnlyList<string> Tags { get; set; } = [];
    }

    private enum BinState
    {
        Open,
        Sealed,
    }
}
