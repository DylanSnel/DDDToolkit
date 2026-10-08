using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The guard a script puts on an event log's table on Postgres: a row is inserted and then stays as it was
/// written. Nothing updates it, nothing truncates the table, and nothing deletes a row before it is older than
/// the table keeps it, whoever asks: the table's owner, a superuser and a session in replication mode included,
/// which a privilege or a policy would not hold back. The apiary keeps its log for thirty days.
/// </summary>
public sealed class EventLogGuardTests(ExplicitCallersPostgres postgres)
{
    private const string Log = "ddd.\"EventLog\"";

    private static readonly TimeSpan KeepFor = LoggedApiaryContext.KeepFor;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static EventLogEntry Entry(string name, TimeSpan age) => new()
    {
        Id = Guid.CreateVersion7(),
        EventName = name,
        Payload = "{}",
        OccurredAt = DateTimeOffset.UtcNow - age,
        RecordedAt = DateTimeOffset.UtcNow - age,
        ActedByKind = ActedByKinds.System,
    };

    /// <summary>Rows written as the superuser, which owns the table: an insert is what the guard lets through.</summary>
    private static async Task SeedAsync(ApiaryDatabase database, params EventLogEntry[] entries)
    {
        await using var seed = database.ModelContext();
        seed.Set<EventLogEntry>().AddRange(entries);
        await seed.SaveChangesAsync(Cancellation);
    }

    private static Task<List<string>> NamesAsync(ApiaryDatabase database)
        => database.ListAsOwnerAsync($"""SELECT "EventName" || ' ' || "Payload" FROM {Log} ORDER BY 1""", Cancellation);

    /// <summary>The guard's refusal, as Postgres raises it: its own state and its own message.</summary>
    private static async Task ShouldBeRefusedAsync(Func<Task> statement, string because)
    {
        var thrown = (await statement.Should().ThrowAsync<Exception>(because)).Which;
        PostgresException? refused = null;
        for (var exception = thrown; exception is not null && refused is null; exception = exception.InnerException)
        {
            refused = exception as PostgresException;
        }

        refused.Should().NotBeNull($"{because}, and what was thrown was {thrown}");
        refused!.SqlState.Should().Be(PostgresRowAccess.KeptRowsSqlState, because);
        refused.MessageText.Should().Be(PostgresRowAccess.KeptRowsRefusal, because);
    }

    [Fact]
    public async Task A_kept_row_cannot_be_updated_even_by_its_owner()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await SeedAsync(database, Entry("young", TimeSpan.FromMinutes(5)), Entry("old", TimeSpan.FromDays(400)));

        await ShouldBeRefusedAsync(
            async () =>
            {
                await using var owner = database.ModelContext();
                await owner.Set<EventLogEntry>().ExecuteUpdateAsync(rows => rows.SetProperty(row => row.Payload, "{\"rewritten\":true}"), Cancellation);
            },
            "an update in the database is refused, of a row old enough to delete as well");

        await ShouldBeRefusedAsync(
            async () =>
            {
                await using var owner = database.ModelContext();
                var row = await owner.Set<EventLogEntry>().SingleAsync(entry => entry.EventName == "old", Cancellation);
                row.ActedByKind = "somebody else";
                await owner.SaveChangesAsync(Cancellation);
            },
            "and so is one through the change tracker");

        (await NamesAsync(database)).Should().Equal("old {}", "young {}");
        (await database.ListAsOwnerAsync($"""SELECT DISTINCT "ActedByKind" FROM {Log}""", Cancellation)).Should().Equal(ActedByKinds.System);
    }

    [Fact]
    public async Task A_kept_row_younger_than_keep_for_cannot_be_deleted()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await SeedAsync(database, Entry("young", KeepFor - TimeSpan.FromHours(1)), Entry("old", TimeSpan.FromDays(400)));

        await ShouldBeRefusedAsync(
            async () =>
            {
                await using var owner = database.ModelContext();
                await owner.Set<EventLogEntry>().ExecuteDeleteAsync(Cancellation);
            },
            "one row of the statement is still kept, so the statement is refused");

        await ShouldBeRefusedAsync(
            async () =>
            {
                await using var owner = database.ModelContext();
                owner.Remove(await owner.Set<EventLogEntry>().SingleAsync(entry => entry.EventName == "young", Cancellation));
                await owner.SaveChangesAsync(Cancellation);
            },
            "and so is removing the young row through the change tracker");

        (await NamesAsync(database)).Should().Equal(["old {}", "young {}"], "a refused statement deletes nothing, the old row included");
    }

    [Fact]
    public async Task An_old_enough_row_may_be_deleted()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await SeedAsync(database, Entry("young", KeepFor - TimeSpan.FromHours(1)), Entry("old", KeepFor + TimeSpan.FromHours(1)));

        await using (var owner = database.ModelContext())
        {
            (await owner.Set<EventLogEntry>().Where(entry => entry.EventName == "old").ExecuteDeleteAsync(Cancellation))
                .Should().Be(1, "the table keeps a row for thirty days, and this one is older");
        }

        (await NamesAsync(database)).Should().Equal("young {}");
    }

    [Fact]
    public async Task Truncating_the_log_is_refused()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await SeedAsync(database, Entry("old", TimeSpan.FromDays(400)));

        // Npgsql leaves a refusal's detail out unless it is asked for; the message and the state are always there.
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.OwnerConnectionString) { IncludeErrorDetail = true }.ConnectionString);
        await connection.OpenAsync(Cancellation);
        var truncate = () => ExecuteAsync(connection, $"TRUNCATE {Log}");

        var refusal = (await truncate.Should().ThrowAsync<PostgresException>("a truncate removes every row at once, the kept ones among them, however old the rest are")).Which;
        refusal.SqlState.Should().Be("55000");
        refusal.MessageText.Should().Be("A kept row does not change.");
        refusal.Detail.Should().Be("TRUNCATE on ddd.\"EventLog\" was refused: the table only grows.");
        (await NamesAsync(database)).Should().ContainSingle();
    }

    [Fact]
    public async Task The_guard_fires_in_replica_mode()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await SeedAsync(database, Entry("young", TimeSpan.FromMinutes(5)));

        // A session that replicates skips ordinary triggers, and a superuser may make its session one.
        await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync(Cancellation);
        await ExecuteAsync(connection, "SET session_replication_role = replica");

        foreach (var statement in (string[])[$"""UPDATE {Log} SET "Payload" = '[]'""", $"DELETE FROM {Log}", $"TRUNCATE {Log}"])
        {
            await ShouldBeRefusedAsync(() => ExecuteAsync(connection, statement), statement);
        }

        (await NamesAsync(database)).Should().Equal("young {}");
        (await database.ListAsOwnerAsync(
            """SELECT tgname || ' ' || tgenabled::text FROM pg_catalog.pg_trigger WHERE tgrelid = 'ddd."EventLog"'::regclass AND NOT tgisinternal ORDER BY 1""", Cancellation))
            .Should().Equal(["ddd_kept_rows A", "ddd_kept_rows_truncate A"], "both triggers are made to fire always");
    }

    [Fact]
    public async Task The_guard_holds_a_role_that_was_granted_everything()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await SeedAsync(database, Entry("young", TimeSpan.FromMinutes(5)));
        await database.RunAsOwnerAsync($"GRANT ALL ON {Log} TO authenticated;", Cancellation);

        await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync(Cancellation);
        await ExecuteAsync(connection, "SET ROLE authenticated");

        // The role may execute nothing in ddd and still meets the trigger: a trigger asks no privilege of whoever fires it.
        await ShouldBeRefusedAsync(() => ExecuteAsync(connection, $"""UPDATE {Log} SET "Payload" = '[]'"""), "an update");
        await ShouldBeRefusedAsync(() => ExecuteAsync(connection, $"DELETE FROM {Log}"), "a delete");

        await ExecuteAsync(connection, $"""INSERT INTO {Log} ("Id", "EventName", "Version", "Payload", "OccurredAt", "RecordedAt", "ActedByKind") VALUES (gen_random_uuid(), 'appended', 1, '[]', now(), now(), 'user')""");
        (await NamesAsync(database)).Should().Equal(["appended []", "young {}"], "a table that only grows still grows");
    }

    [Fact]
    public async Task A_log_without_keep_for_never_lets_a_row_go()
    {
        var owner = await postgres.CreateDatabaseAsync(Cancellation);
        await using var annals = new AnnalsContext(new DbContextOptionsBuilder<AnnalsContext>().UseNpgsql(owner).Options);
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync(Cancellation);
        await ExecuteAsync(connection, annals.Database.GenerateCreateScript());
        var script = PostgresRowAccess.Script(annals, []);
        await ExecuteAsync(connection, script);

        annals.Annals.Add(Entry("ancient", TimeSpan.FromDays(3650)));
        await annals.SaveChangesAsync(Cancellation);

        await ShouldBeRefusedAsync(
            () => annals.Annals.ExecuteDeleteAsync(Cancellation),
            "a log mapped without keepFor keeps every row, however old, under whatever name and schema it has");

        // The next script is run by the role that owns the table, which is not the one that made the function:
        // the function is left alone while it is as the script needs it, and the triggers are the owner's to replace.
        var tableOwner = $"annals_owner_{Guid.NewGuid():N}"[..30];
        await ExecuteAsync(connection, $"""
            CREATE ROLE {tableOwner} NOLOGIN;
            GRANT USAGE ON SCHEMA ddd, archive TO {tableOwner};
            ALTER TABLE archive."Annals" OWNER TO {tableOwner};
            SET ROLE {tableOwner};
            """);
        await ExecuteAsync(connection, script);

        await ShouldBeRefusedAsync(() => ExecuteAsync(connection, """DELETE FROM archive."Annals" """), "the owner of the table is held to it too");
        await ExecuteAsync(connection, $"RESET ROLE; DROP OWNED BY {tableOwner}; DROP ROLE {tableOwner};");
    }

    [Fact]
    public async Task The_next_script_follows_the_model()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await SeedAsync(database, Entry("forty days old", TimeSpan.FromDays(40)));

        // The model now keeps a row for a year: the next script writes the guard again, with the new period.
        await using var year = new YearLoggedApiaryContext(new DbContextOptionsBuilder<YearLoggedApiaryContext>().UseNpgsql(database.OwnerConnectionString).Options);
        var script = PostgresRowAccess.Script(year, ApiaryRules.All, [], database.Export);
        await database.RunAsOwnerAsync(script, Cancellation);

        await ShouldBeRefusedAsync(
            async () =>
            {
                await using var owner = database.ModelContext();
                await owner.Set<EventLogEntry>().ExecuteDeleteAsync(Cancellation);
            },
            "forty days is past the old period and inside the new one");

        // And back to thirty days, by the script of the model that says so.
        await using var month = database.ModelContext();
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(month, ApiaryRules.All, [], database.Export), Cancellation);
        (await month.Set<EventLogEntry>().ExecuteDeleteAsync(Cancellation)).Should().Be(1);

        (await database.ListAsOwnerAsync("SELECT count(*) FROM pg_catalog.pg_proc WHERE proname = 'refuse_changes_to_kept_rows'", Cancellation)).Should().Equal(["1"], "the function is made once and left alone while it is as the script needs it");
    }

    [Fact]
    public void A_script_ends_with_the_guard_of_the_contexts_log_and_one_of_a_context_without_is_as_it_was()
    {
        using var logged = LoggedApiaryContext.ForScripts();
        using var plain = ApiaryContext.ForScripts();
        var export = new RowAccessExport { Roles = RowAccessRoleNames.From(ApiaryDatabase.Roles()) };

        var script = PostgresRowAccess.Script(logged, ApiaryRules.All, [], export);

        script.Should().Contain("        EXECUTE 'CREATE OR REPLACE FUNCTION ddd.refuse_changes_to_kept_rows() RETURNS trigger LANGUAGE plpgsql SET search_path = '''' AS ' || pg_catalog.quote_literal(body);\n")
            .And.EndWith(
                "CREATE OR REPLACE TRIGGER ddd_kept_rows BEFORE UPDATE OR DELETE ON ddd.\"EventLog\"\n" +
                "    FOR EACH ROW EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows('2592000', 'RecordedAt');\n" +
                "ALTER TABLE ddd.\"EventLog\" ENABLE ALWAYS TRIGGER ddd_kept_rows;\n" +
                "CREATE OR REPLACE TRIGGER ddd_kept_rows_truncate BEFORE TRUNCATE ON ddd.\"EventLog\"\n" +
                "    FOR EACH STATEMENT EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows();\n" +
                "ALTER TABLE ddd.\"EventLog\" ENABLE ALWAYS TRIGGER ddd_kept_rows_truncate;\n");
        script.Should().NotContain($"ALTER TABLE {Log} ENABLE ROW LEVEL SECURITY", "a log nobody claims has no policies, like the outbox");

        PostgresRowAccess.Script(plain, ApiaryRules.All, [], export).Should().NotContain("kept_rows");
        PostgresRowAccess.WritesFor(logged, export).Should().BeTrue("a context with a log has a script to write even without a rule");
        PostgresRowAccess.WritesFor(plain, export).Should().BeFalse("an outbox and an inbox alone ask for a script only where it writes privileges");
        PostgresRowAccess.WritesFor(plain, new RowAccessExport { WriteGrants = true }).Should().BeTrue();
        using var desk = DeskContext.Create();
        PostgresRowAccess.WritesFor(desk, new RowAccessExport { WriteGrants = true }).Should().BeFalse("a context with none of the toolkit's tables has nothing to say without a rule");
        PostgresRowAccess.WritesFor(desk, new RowAccessExport { WriteGrants = true, Roles = RowAccessRoleNames.Default with { System = "ddd_system" } })
            .Should().BeTrue("a bookkeeping role reads every context's migration history, which only a script gives it");
        PostgresRowAccess.WritesFor(desk, new RowAccessExport { Roles = RowAccessRoleNames.Default with { System = "ddd_system" } })
            .Should().BeFalse("a script that writes no privileges gives the bookkeeping role nothing");
    }

    [Fact]
    public async Task The_log_takes_a_row_from_whoever_saves_and_gives_none_back()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        await using var host = database.BuildHost(services => services.AddSingleton<IEventLogFields, YardOfTheFlow>());

        using (YardOfTheFlow.Begin("north"))
        {
            await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
            {
                context.Hives.Add(new Hive(HiveId.CreateSequential(), number: 1, "By the hedge", ApiaryDatabase.AliceId, isOpen: true));
                await context.SaveChangesAsync(Cancellation);
            });
        }

        (await database.ListAsOwnerAsync($"""SELECT "EventName" || ' by ' || "ActedByKind" || ' ' || "ActedById" || ' in ' || "Yard" FROM {Log}""", Cancellation))
            .Should().Equal([$"hive.settled by user {ApiaryDatabase.AliceId} in north"], "the keeper's save wrote the row, with who acted and the module's own column");

        (await database.PrivilegesAsync(Cancellation)).Where(privilege => privilege.StartsWith("ddd.EventLog ", StringComparison.Ordinal)).Should().Equal(
            [
                "ddd.EventLog authenticated INSERT",
                "ddd.EventLog ddd_system DELETE",
                "ddd.EventLog ddd_system SELECT(Id)",
                "ddd.EventLog ddd_system SELECT(RecordedAt)",
                "ddd.EventLog ddd_system_in INSERT",
            ],
            "whoever saves adds rows, the bookkeeping finds the rows that may go, and nobody reads a payload");

        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            var read = () => context.Set<EventLogEntry>().CountAsync(Cancellation);
            (await read.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        });
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}
