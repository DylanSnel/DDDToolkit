using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The privileges a script writes from its policies, against a real Postgres, under a login role that holds
/// nothing: every rule of the apiary runs with exactly them, and whatever no rule allows is refused by the
/// database before a policy is asked.
/// </summary>
public sealed class RowAccessPrivilegePostgresTests(ExplicitCallersPostgres postgres)
{
    private const string Hives = ApiaryContext.Schema + ".\"Hives\"";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// What the privileges per column rest on: Postgres refuses an update that writes a column the role was not
    /// granted, with the same state as any missing privilege, and lets through one that writes only granted
    /// columns, reading the row by the columns it may select.
    /// </summary>
    [Fact]
    public async Task A_column_update_without_its_privilege_is_42501_on_npgsql()
    {
        var owner = await postgres.CreateDatabaseAsync(Cancellation);
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync(Cancellation);
        await ExecuteAsync(connection,
            """
            CREATE TABLE probe_shelf ("Id" uuid PRIMARY KEY, "Title" text NOT NULL, "Rank" integer NOT NULL);
            INSERT INTO probe_shelf VALUES ('00000000-0000-4000-8000-000000000001', 'first', 1);
            GRANT SELECT ON probe_shelf TO authenticated;
            GRANT UPDATE ("Title") ON probe_shelf TO authenticated;
            SET ROLE authenticated;
            """);

        (await ExecuteAsync(connection, """UPDATE probe_shelf SET "Title" = 'second' WHERE "Id" = '00000000-0000-4000-8000-000000000001'""")).Should().Be(1, "the column is granted, and the row is found by a column the role may read");

        foreach (var statement in (string[])["""UPDATE probe_shelf SET "Rank" = 2""", """UPDATE probe_shelf SET "Title" = 'third', "Rank" = 3"""])
        {
            var refused = (await FluentActions.Awaiting(() => ExecuteAsync(connection, statement)).Should().ThrowAsync<PostgresException>(statement)).Which;
            refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "one column without the privilege refuses the whole statement");
            refused.MessageText.Should().Be("permission denied for table probe_shelf", "and the message names the table, not a policy");
        }

        (await ScalarAsync(connection, """SELECT "Title" || ' ' || "Rank" FROM probe_shelf""")).Should().Be("second 1");
    }

    [Fact]
    public async Task The_generated_privileges_let_every_rule_run_and_nothing_more()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        await using var host = database.BuildHost();
        var open = HiveId.CreateSequential();
        var closed = HiveId.CreateSequential();

        // A keeper does anything with their own hives: the hive, its boxes and the event's outbox row in one save.
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            var hive = new Hive(open, number: 1, "By the hedge", ApiaryDatabase.AliceId, isOpen: true);
            hive.Stack("brood");
            hive.Stack("honey");
            context.Hives.AddRange(hive, new Hive(closed, number: 2, "Behind the shed", ApiaryDatabase.AliceId, isOpen: false));
            await context.SaveChangesAsync(Cancellation);
        });
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            var hive = await context.Hives.SingleAsync(row => row.Id == open, Cancellation);
            hive.Rename("By the gate");
            hive.Refit("nursery");
            hive.Unstack();
            hive.Stack("comb");
            await context.SaveChangesAsync(Cancellation);
        });

        // Whoever signed in reads every hive, and changes only their own: the policy refuses, not a privilege.
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Bob, async context =>
        {
            var hive = await context.Hives.SingleAsync(row => row.Id == open, Cancellation);
            hive.Boxes.Select(box => box.Kind).Should().BeEquivalentTo(["nursery", "comb"], "the keeper's update, delete and insert of boxes all ran");
            hive.Rename("Bob's now");
            var save = () => context.SaveChangesAsync(Cancellation);
            (await save.Should().ThrowAsync<RefusalException>("the row is Bob's to read and not to change")).Which.Code.Should().Be(ToolkitRefusals.Refused);
        });

        // A visitor reads the open hives, and adds none: no rule lets a visitor write, so there is no privilege to.
        await ApiaryDatabase.AsAsync(host, Caller.Anonymous, async context =>
        {
            (await context.Hives.Select(hive => hive.Number).ToListAsync(Cancellation)).Should().Equal(1);
            context.Hives.Add(new Hive(HiveId.CreateSequential(), number: 3, "A visitor's", keeper: null, isOpen: true));
            var save = () => context.SaveChangesAsync(Cancellation);
            ShouldLackThePrivilege((await save.Should().ThrowAsync<DbUpdateException>()).Which.InnerException, "table Hives");
        });

        // A ranger reads every hive and removes none.
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Ranger, async context =>
        {
            (await ScalarAsync(context, """SELECT current_user::text AS "Value" """)).Should().Be(ApiaryRules.RangerRole);
            (await context.Hives.CountAsync(Cancellation)).Should().Be(2);
            var remove = () => context.Database.ExecuteSqlRawAsync($"DELETE FROM {Hives}", Cancellation);
            ShouldLackThePrivilege((await remove.Should().ThrowAsync<PostgresException>()).Which, "table Hives");
        });

        // The outbox took a row from every save that raised an event, and nobody who saves reads one back.
        (await database.ListAsOwnerAsync("""SELECT "EventName" FROM ddd."OutboxMessages" ORDER BY "EventName" """, Cancellation))
            .Should().Equal("hive.renamed", "hive.settled", "hive.settled");
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            var read = () => context.Outbox.CountAsync(Cancellation);
            ShouldLackThePrivilege((await read.Should().ThrowAsync<PostgresException>()).Which, "table OutboxMessages");
        });

        // Scoped system work writes the inbox, as the handlers of integration events do, and reaches no hive.
        await ApiaryDatabase.AsAsync(host, Caller.SystemIn("apiary"), async context =>
        {
            context.Inbox.Add(new InboxMessage { MessageId = Guid.CreateVersion7(), Consumer = "apiary.test", ProcessedAt = DateTimeOffset.UtcNow });
            await context.SaveChangesAsync(Cancellation);
            (await context.Inbox.CountAsync(Cancellation)).Should().Be(1);
            var read = () => context.Hives.CountAsync(Cancellation);
            ShouldLackThePrivilege((await read.Should().ThrowAsync<PostgresException>("no rule lets scoped work near a hive, so it does not even get into the schema")).Which, "schema apiary");
        });

        // And nothing more: what the catalog holds is exactly what the policies allow.
        (await database.PrivilegesAsync(Cancellation)).Should().Equal(
            [
                "apiary.HiveBox anon SELECT",
                "apiary.HiveBox apiary_ranger SELECT",
                "apiary.HiveBox authenticated DELETE",
                "apiary.HiveBox authenticated INSERT",
                "apiary.HiveBox authenticated SELECT",
                "apiary.HiveBox authenticated UPDATE(Kind)",
                "apiary.Hives anon SELECT",
                "apiary.Hives apiary_ranger SELECT",
                "apiary.Hives authenticated DELETE",
                "apiary.Hives authenticated INSERT",
                "apiary.Hives authenticated SELECT",
                "apiary.Hives authenticated UPDATE(IsOpen)",
                "apiary.Hives authenticated UPDATE(Keeper)",
                "apiary.Hives authenticated UPDATE(Label)",
                "apiary.Hives authenticated UPDATE(Version)",
                "ddd.InboxMessages ddd_system DELETE",
                "ddd.InboxMessages ddd_system INSERT",
                "ddd.InboxMessages ddd_system SELECT",
                "ddd.InboxMessages ddd_system UPDATE",
                "ddd.InboxMessages ddd_system_in INSERT",
                "ddd.InboxMessages ddd_system_in SELECT",
                "ddd.OutboxMessages authenticated INSERT",
                "ddd.OutboxMessages ddd_system DELETE",
                "ddd.OutboxMessages ddd_system SELECT",
                "ddd.OutboxMessages ddd_system UPDATE(Attempts)",
                "ddd.OutboxMessages ddd_system UPDATE(LastError)",
                "ddd.OutboxMessages ddd_system UPDATE(NextAttemptAt)",
                "ddd.OutboxMessages ddd_system UPDATE(ProcessedAt)",
                "ddd.OutboxMessages ddd_system_in INSERT",
            ]);

        // The keeper removes a hive, boxes and all.
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            context.Hives.Remove(await context.Hives.SingleAsync(row => row.Id == open, Cancellation));
            await context.SaveChangesAsync(Cancellation);
        });
        (await database.ListAsOwnerAsync($"SELECT count(*) FROM {Hives}", Cancellation)).Should().Equal("1");
    }

    [Fact]
    public async Task A_column_fixed_after_insert_cannot_be_updated_by_a_query()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        await using var host = database.BuildHost();
        var id = HiveId.CreateSequential();
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            context.Hives.Add(new Hive(id, number: 7, "By the hedge", ApiaryDatabase.AliceId, isOpen: true));
            await context.SaveChangesAsync(Cancellation);
        });

        // A statement that goes around the model, as the keeper whose hive it is: the policy would let it through.
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            var renumber = () => context.Database.ExecuteSqlRawAsync($"""UPDATE {Hives} SET "Number" = 8""", Cancellation);
            ShouldLackThePrivilege((await renumber.Should().ThrowAsync<PostgresException>("the number is fixed once the hive stands")).Which, "table Hives");

            var rekey = () => context.Database.ExecuteSqlRawAsync($"""UPDATE {Hives} SET "Id" = '00000000-0000-4000-8000-000000000009'""", Cancellation);
            ShouldLackThePrivilege((await rekey.Should().ThrowAsync<PostgresException>("and so is its key")).Which, "table Hives");

            (await context.Database.ExecuteSqlRawAsync($"""UPDATE {Hives} SET "Label" = 'By the gate'""", Cancellation)).Should().Be(1, "a column that may change still does");
        });

        (await database.ListAsOwnerAsync($"""SELECT "Number" || ' ' || "Label" FROM {Hives}""", Cancellation)).Should().Equal("7 By the gate");
    }

    [Fact]
    public async Task The_bookkeeping_role_marks_a_pending_event_and_cannot_rewrite_it()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);

        // A handler that fails the first delivery, so the processor writes every column it ever writes: the
        // attempt, the error and when to try again, and then the moment it was delivered.
        await using var host = database.BuildHost();
        var recorder = host.GetRequiredService<EventRecorder>();
        recorder.OnEvent = (_, _, _) => Task.FromException(new InvalidOperationException("The yard is closed."));
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            context.Hives.Add(new Hive(HiveId.CreateSequential(), number: 1, "By the hedge", ApiaryDatabase.AliceId, isOpen: true));
            await context.SaveChangesAsync(Cancellation);
        });

        async Task<int> ProcessAsync()
        {
            await using var scope = host.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OutboxProcessor<ApiaryContext>>().ProcessPendingAsync(cancellationToken: Cancellation);
        }

        (await ProcessAsync()).Should().Be(0);
        (await database.ListAsOwnerAsync("""SELECT "Attempts" || ' ' || "LastError" || ' ' || ("NextAttemptAt" IS NOT NULL)::text || ' ' || ("ProcessedAt" IS NULL)::text FROM ddd."OutboxMessages" """, Cancellation))
            .Should().ContainSingle().Which.Should().StartWith("1 ").And.Contain("The yard is closed.").And.EndWith(" true true", "the failed attempt was recorded under the written privileges");

        recorder.OnEvent = null;
        await database.RunAsOwnerAsync("""UPDATE ddd."OutboxMessages" SET "NextAttemptAt" = NULL""", Cancellation);
        (await ProcessAsync()).Should().Be(1);
        (await database.ListAsOwnerAsync("""SELECT "Attempts" || ' ' || ("LastError" IS NULL)::text || ' ' || ("ProcessedAt" IS NOT NULL)::text FROM ddd."OutboxMessages" """, Cancellation))
            .Should().Equal(["2 true true"], "and so was the delivery");

        // The same role, in a statement that goes around the processor: how a delivery went is its to write, the
        // event is not. An event nobody delivered yet would otherwise be whatever the last statement made of it.
        await ApiaryDatabase.AsAsync(host, Caller.System, async context =>
        {
            (await ScalarAsync(context, """SELECT current_user::text AS "Value" """)).Should().Be(ApiaryDatabase.SystemRole);

            foreach (var column in (string[])["Payload", "EventName", "Version", "OccurredAt", "AggregateId", "CreatedAt", "Id"])
            {
                var statement = "UPDATE ddd.\"OutboxMessages\" SET \"" + column + "\" = \"" + column + "\"";
                var rewrite = () => context.Database.ExecuteSqlRawAsync(statement, Cancellation);
                ShouldLackThePrivilege((await rewrite.Should().ThrowAsync<PostgresException>(column)).Which, "table OutboxMessages");
            }

            (await context.Database.ExecuteSqlRawAsync("""UPDATE ddd."OutboxMessages" SET "ProcessedAt" = NULL, "Attempts" = 0, "NextAttemptAt" = NULL, "LastError" = NULL""", Cancellation))
                .Should().Be(1, "resetting a row so it is delivered again is bookkeeping");
        });
    }

    [Fact]
    public async Task A_script_for_other_roles_takes_the_toolkits_own_tables_back_from_the_roles_before()
    {
        // The first file: a bookkeeping role, and a ranger who may remove a hive, so a ranger's save adds to the outbox and the log.
        var database = await ApiaryDatabase.CreateAsync(
            postgres,
            logged: true,
            rules: [.. ApiaryRules.All, RowAccessRule.For<Hive>("Rangers condemn a hive", RowOperations.Remove, "TRUE", RowAccessRoles.Token(ApiaryRules.Ranger))]);
        using var model = database.ModelContext();

        static bool OnTheToolkitsTables(string privilege) => privilege.StartsWith("ddd.", StringComparison.Ordinal);
        var before = (await database.PrivilegesAsync(Cancellation)).Where(OnTheToolkitsTables).ToList();
        before.Should().Contain(["ddd.OutboxMessages ddd_system SELECT", "ddd.EventLog ddd_system SELECT(RecordedAt)", "ddd.OutboxMessages apiary_ranger INSERT", "ddd.EventLog apiary_ranger INSERT"]);

        // What the host gave by hand to roles no file ever names: the role its reports run as, which is held to the
        // policies like any caller's, and a role that is past them, as the one a host's own background work runs as.
        var reporting = NewRole("reports");
        var service = NewRole("service");
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {reporting} NOLOGIN;
            CREATE ROLE {service} NOLOGIN BYPASSRLS;
            GRANT SELECT ON ddd."OutboxMessages" TO {reporting};
            GRANT SELECT ("EventName") ON ddd."EventLog" TO {reporting};
            GRANT SELECT, UPDATE, DELETE ON ddd."OutboxMessages" TO {service};
            GRANT SELECT ON apiary."Hives" TO {reporting};
            """,
            Cancellation);

        try
        {
            // The second file: the bookkeeping runs as another role, and no token role is mapped any more.
            var books = NewRole("books");
            var export = new RowAccessExport { Roles = RowAccessRoleNames.Default with { System = books }, WriteGrants = true };
            await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [ApiaryRules.Keepers, ApiaryRules.SignedIn, ApiaryRules.Visitors], [], export), Cancellation);

            var after = await database.PrivilegesAsync(Cancellation);
            after.Where(OnTheToolkitsTables).Should().Equal(
                [.. before
                    .Where(privilege => !privilege.Contains(" apiary_ranger ", StringComparison.Ordinal))
                    .Select(privilege => privilege.Replace(" ddd_system ", $" {books} ", StringComparison.Ordinal))
                    .Append($"ddd.OutboxMessages {service} DELETE")
                    .Append($"ddd.OutboxMessages {service} SELECT")
                    .Append($"ddd.OutboxMessages {service} UPDATE")
                    .Order(StringComparer.Ordinal)],
                "the first bookkeeping role and the ranger's role hold nothing on the outbox, the inbox or the log any more, the reports' role neither, and the role that is past the policies keeps what the host gave it");

            // The login role may still switch to the first bookkeeping role, and finds nothing there.
            await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
            await connection.OpenAsync(Cancellation);
            await ExecuteAsync(connection, $"SET ROLE {ApiaryDatabase.SystemRole}");
            var read = () => ScalarAsync(connection, """SELECT count(*) FROM ddd."OutboxMessages" """);
            ShouldLackThePrivilege((await read.Should().ThrowAsync<PostgresException>()).Which, "table OutboxMessages");

            // A table with policies is another matter: a role of the host's own keeps what it held there.
            after.Should().Contain($"apiary.Hives {reporting} SELECT");

            await database.RunAsOwnerAsync($"DROP OWNED BY {books}; DROP ROLE {books};", Cancellation);
        }
        finally
        {
            await database.RunAsOwnerAsync($"DROP OWNED BY {reporting}, {service}; DROP ROLE {reporting}, {service};", Cancellation);
        }
    }

    [Fact]
    public async Task A_sequence_is_handed_to_the_roles_that_add_rows_and_to_nobody_else()
    {
        var owner = await postgres.CreateDatabaseAsync(Cancellation);
        await using var ledger = RowAccessPrivilegeTests.LedgerContext.Create(owner);
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync(Cancellation);

        // The tables as a migration makes them, with privileges on the table alone, as a grant by hand gives them.
        await ExecuteAsync(connection, PostgresRowAccess.SetupScript());
        await ExecuteAsync(connection, ledger.Database.GenerateCreateScript());
        await ExecuteAsync(connection, """GRANT USAGE ON SCHEMA ledger TO anon, authenticated; GRANT SELECT, INSERT ON ledger."Entries" TO anon, authenticated;""");

        const string add =
            """
            INSERT INTO ledger."Entries" ("Id", "Code", "BookedBy", "Net", "Tax", "Kind", "Period_From", "Period_Until", "Trail")
            VALUES (gen_random_uuid(), gen_random_uuid()::text, gen_random_uuid(), 10, 2, 'entry', DATE '2026-01-01', DATE '2026-01-31', '{"Source":"desk","Steps":[]}')
            RETURNING "Serial" || ' ' || "Folio"
            """;

        // What the block is for: the privilege on the table is not enough for a column that takes its value from a sequence.
        await ExecuteAsync(connection, "SET ROLE authenticated");
        var unhanded = () => ScalarAsync(connection, add);
        var refused = (await unhanded.Should().ThrowAsync<PostgresException>()).Which;
        refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        refused.MessageText.Should().StartWith("permission denied for sequence ");
        await ExecuteAsync(connection, "RESET ROLE");

        await ExecuteAsync(connection, PostgresRowAccess.Script(ledger, RowAccessPrivilegeTests.LedgerRules, [], new RowAccessExport { WriteGrants = true }));

        var sequences = await ListAsync(
            connection,
            """
            SELECT a.attname || ' ' || pg_catalog.has_sequence_privilege('authenticated', s.sequence, 'USAGE')::text || ' ' || pg_catalog.has_sequence_privilege('anon', s.sequence, 'USAGE')::text
                   || ' ' || pg_catalog.has_sequence_privilege('ddd_system_in', s.sequence, 'USAGE')::text
            FROM pg_catalog.pg_attribute a
            CROSS JOIN LATERAL (SELECT pg_catalog.pg_get_serial_sequence('ledger."Entries"', a.attname) AS sequence) s
            WHERE a.attrelid = 'ledger."Entries"'::pg_catalog.regclass AND s.sequence IS NOT NULL
            ORDER BY 1
            """);
        sequences.Should().Equal(["Folio true false false", "Serial true false false"], "a bookkeeper adds entries, and a visitor only reads them");

        // A bookkeeper adds an entry, and the database hands out both numbers.
        await ExecuteAsync(connection, "SET ROLE authenticated");
        (await ScalarAsync(connection, add)).Should().Be("1 1");
        (await ScalarAsync(connection, add)).Should().Be("2 2");

        // A visitor reads, and neither adds a row nor takes a number.
        await ExecuteAsync(connection, "SET ROLE anon");
        (await ScalarAsync(connection, """SELECT count(*) FROM ledger."Entries" """)).Should().Be("2");
        var visitorAdds = () => ScalarAsync(connection, add);
        ShouldLackThePrivilege((await visitorAdds.Should().ThrowAsync<PostgresException>()).Which, "table Entries");
        var visitorTakes = () => ScalarAsync(connection, """SELECT nextval(pg_catalog.pg_get_serial_sequence('ledger."Entries"', 'Serial'))""");
        (await visitorTakes.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static int roles;

    /// <summary>A role name no other test uses: roles are the server's, and every test class shares one.</summary>
    private static string NewRole(string what) => $"apiary_{what}_{Interlocked.Increment(ref roles)}_{Guid.NewGuid():N}"[..40];

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        var rows = new List<string>();
        while (await reader.ReadAsync(Cancellation))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    /// <summary>A missing privilege, as Postgres says it: its own state, and a message that names what the role may not touch, and no policy.</summary>
    private static void ShouldLackThePrivilege(Exception? exception, string what)
    {
        var refused = exception.Should().BeOfType<PostgresException>().Subject;
        refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        refused.MessageText.Should().Be($"permission denied for {what}");
    }

    /// <summary>The one value <paramref name="sql"/> answers, which names its column <c>"Value"</c>, asked through the context.</summary>
    private static async Task<string?> ScalarAsync(DbContext context, string sql)
        => (await context.Database.SqlQueryRaw<string>(sql).ToListAsync(Cancellation)).Single();

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(Cancellation), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(Cancellation);
    }
}
