using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The tables of an aggregate's entities on a Postgres that is not Supabase's: a caller reads a comment,
/// a watcher or a reaction when it may read the ticket, and writes one only as the ticket's write rules let
/// it write the ticket. Most of these write by SQL, as the Data API or a query of the application's own
/// would: such SQL reaches the entities' tables without touching the ticket, so only the entities' own
/// policies stand in its way.
/// </summary>
/// <remarks>
/// A fixture of its own, because these tests add rows the reading tests would count.
/// </remarks>
public sealed class PostgresRowAccessEntityTests(PostgresRowAccessDatabase database) : IClassFixture<PostgresRowAccessDatabase>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Caller Alice = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Alice, team: "north"));

    private static readonly Caller Bob = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Bob, team: "north"));

    private static readonly Caller Carol = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Carol, team: "south"));

    private const string AddComment = """INSERT INTO desk."TicketComment" ("Id", "TicketId", "Text") VALUES ({0}, {1}, {2})""";

    private const string ChangeComments = """UPDATE desk."TicketComment" SET "Text" = 'changed' WHERE "TicketId" = {0}""";

    private const string RemoveComments = """DELETE FROM desk."TicketComment" WHERE "TicketId" = {0}""";

    private const string TouchTicket = """UPDATE desk."Tickets" SET "Title" = "Title" WHERE "Id" = {0}""";

    private const string AddWatcher = """INSERT INTO desk."TicketWatcher" ("Id", "TicketId", "User") VALUES ({0}, {1}, {2})""";

    private const string RemoveWatchers = """DELETE FROM desk."TicketWatcher" WHERE "TicketId" = {0}""";

    private const string AddReaction = """INSERT INTO desk."CommentReaction" ("Id", "TicketCommentTicketId", "TicketCommentId", "User") VALUES ({0}, {1}, {2}, {3})""";

    private const string RemoveReactions = """DELETE FROM desk."CommentReaction" WHERE "TicketCommentTicketId" = {0}""";

    // The probe: what ddd.written_in_this_transaction says, as the superuser, whom no policy concerns.

    [Fact]
    public async Task A_row_written_in_this_transaction_is_new_inside_a_savepoint_and_nowhere_else()
    {
        database.Require();
        await using var mine = await database.OpenAsOwnerAsync(Cancellation);
        await using var theirs = await database.OpenAsOwnerAsync(Cancellation);
        var table = await ProbeTableAsync(mine);

        await RunAsync(mine, $"INSERT INTO {table} VALUES (1)");
        await RunAsync(mine, "BEGIN");
        await RunAsync(mine, $"INSERT INTO {table} VALUES (2)");
        await RunAsync(mine, "SAVEPOINT outer_one");
        await RunAsync(mine, $"INSERT INTO {table} VALUES (3)");
        await RunAsync(mine, "SAVEPOINT inner_one");
        await RunAsync(mine, $"INSERT INTO {table} VALUES (4)");

        (await WrittenAsync(mine, table)).Should().Equal(
            [(1, false), (2, true), (3, true), (4, true)],
            "a savepoint's rows carry the savepoint's own transaction id, which comes after the transaction's");

        await RunAsync(mine, "RELEASE SAVEPOINT inner_one");
        await RunAsync(mine, "RELEASE SAVEPOINT outer_one");

        // A transaction that got its id after this one did, and committed while this one still runs.
        await RunAsync(theirs, $"INSERT INTO {table} VALUES (5)");

        (await WrittenAsync(mine, table)).Should().Equal(
            [(1, false), (2, true), (3, true), (4, true), (5, false)],
            "a released savepoint is still this transaction's, and a row committed by a later one is not, and is no error either");

        await RunAsync(mine, "COMMIT");

        (await WrittenAsync(mine, table)).Should().OnlyContain(row => !row.Written, "the next transaction wrote none of them");
        (await WrittenAsync(theirs, table)).Should().OnlyContain(row => !row.Written, "another connection wrote none of them either");
    }

    [Fact]
    public async Task A_root_updated_in_this_transaction_counts_as_written()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        var table = await ProbeTableAsync(connection);
        await RunAsync(connection, $"INSERT INTO {table} VALUES (1), (2), (3)");

        await RunAsync(connection, "BEGIN");
        await RunAsync(connection, $"UPDATE {table} SET id = id WHERE id = 1");
        await RunAsync(connection, $"SELECT id FROM {table} WHERE id = 2 FOR UPDATE");

        (await WrittenAsync(connection, table)).Should().Equal(
            [(1, true), (2, false), (3, false)],
            "an update writes a new version of the row, which this transaction wrote; a lock writes none");

        await RunAsync(connection, "ROLLBACK");
    }

    // What each caller may do with the entities.

    [Fact]
    public async Task Anonymous_cannot_add_change_or_remove_a_comment_on_the_public_ticket()
    {
        database.Require();
        var ticket = await TicketAsync("Everyone's");

        var add = () => database.RunAsAsync(Caller.Anonymous, AddComment, Cancellation, Guid.NewGuid(), ticket, "anonymous was here");

        await RefusedAsync(add);
        (await database.RunAsAsync(Caller.Anonymous, ChangeComments, Cancellation, ticket)).Should().Be(0, "anonymous reads the comment, and may change nothing of the ticket's");
        (await database.RunAsAsync(Caller.Anonymous, RemoveComments, Cancellation, ticket)).Should().Be(0);
        (await CommentsOfAsync(ticket)).Should().Equal(["On everyone's"]);
    }

    [Fact]
    public async Task A_teammate_who_only_reads_cannot_write_the_comments_or_watchers()
    {
        database.Require();
        var ticket = await TicketAsync("Alice's, open for the north");
        (await database.CommentsAsync(Bob, Cancellation)).Should().Contain("On Alice's", "Bob reads the open tickets of the north, and their comments");

        var addComment = () => database.RunAsAsync(Bob, AddComment, Cancellation, Guid.NewGuid(), ticket, "Bob was here");
        var watch = () => database.RunAsAsync(Bob, AddWatcher, Cancellation, Guid.NewGuid(), ticket, PostgresRowAccessDatabase.Bob);

        await RefusedAsync(addComment);
        await RefusedAsync(watch);
        (await database.RunAsAsync(Bob, ChangeComments, Cancellation, ticket)).Should().Be(0);
        (await database.RunAsAsync(Bob, RemoveComments, Cancellation, ticket)).Should().Be(0);
        (await CommentsOfAsync(ticket)).Should().Equal(["On Alice's"]);
    }

    [Fact]
    public async Task The_owner_adds_comments_to_a_new_ticket_in_the_same_save()
    {
        database.Require();
        var ticket = new Ticket(TicketId.CreateSequential(), "Alice's new one", PostgresRowAccessDatabase.Alice, team: null, TicketStatus.Open, isPublic: false);
        ticket.Comment("First");
        ticket.Comment("Second");
        ticket.Comments[0].React(PostgresRowAccessDatabase.Alice);
        ticket.Watch(PostgresRowAccessDatabase.Carol);

        await using (var context = database.CreateContext(Alice))
        {
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync(Cancellation);
        }

        (await CommentsOfAsync(ticket.Id.Value)).Should().BeEquivalentTo(["First", "Second"]);
        (await ReactionsOfAsync(ticket.Id.Value)).Should().Be(1);
    }

    [Fact]
    public async Task The_owner_adds_comments_to_a_new_ticket_inside_a_user_transaction()
    {
        database.Require();
        var ticket = new Ticket(TicketId.CreateSequential(), "Alice's, in a transaction", PostgresRowAccessDatabase.Alice, team: null, TicketStatus.Open, isPublic: false);
        ticket.Comment("Inside");
        ticket.Comments[0].React(PostgresRowAccessDatabase.Alice);

        await using (var context = database.CreateContext(Alice))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync(Cancellation);
            await transaction.CommitAsync(Cancellation);
        }

        (await CommentsOfAsync(ticket.Id.Value)).Should().Equal(["Inside"]);
        (await ReactionsOfAsync(ticket.Id.Value)).Should().Be(1);
    }

    [Fact]
    public async Task A_teammate_who_files_a_ticket_for_the_team_adds_its_first_comments_in_the_same_save()
    {
        database.Require();
        var ticket = new Ticket(TicketId.CreateSequential(), "Filed by Bob", owner: null, "north", TicketStatus.Open, isPublic: false);
        ticket.Comment("It broke");
        ticket.Comments[0].React(PostgresRowAccessDatabase.Bob);

        await using (var context = database.CreateContext(Bob))
        {
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync(Cancellation);
        }

        var later = () => database.RunAsAsync(Bob, AddComment, Cancellation, Guid.NewGuid(), ticket.Id.Value, "And another thing");

        (await CommentsOfAsync(ticket.Id.Value)).Should().Equal(["It broke"]);
        (await ReactionsOfAsync(ticket.Id.Value)).Should().Be(1, "the reaction, two tables away, asks the same ticket");
        await RefusedAsync(later, "Bob may create the ticket, not change it, and it is no longer new");
    }

    [Fact]
    public async Task A_teammate_who_files_a_ticket_for_the_team_adds_its_first_comments_inside_a_user_transaction()
    {
        database.Require();
        var ticket = new Ticket(TicketId.CreateSequential(), "Filed by Bob, in a transaction", owner: null, "north", TicketStatus.Open, isPublic: false);
        ticket.Comment("Inside");
        ticket.Comments[0].React(PostgresRowAccessDatabase.Bob);

        await using (var context = database.CreateContext(Bob))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync(Cancellation);

            var bySavepoint = await context.Database
                .SqlQueryRaw<bool>(
                    """SELECT "xmin"::text::bigint <> (pg_catalog.pg_current_xact_id()::text::bigint & 4294967295) AS "Value" FROM desk."Tickets" WHERE "Id" = {0}""",
                    ticket.Id.Value)
                .SingleAsync(Cancellation);
            bySavepoint.Should().BeTrue("Entity Framework saves inside a transaction of the caller's own behind a savepoint, so the ticket carries the savepoint's id");

            await transaction.CommitAsync(Cancellation);
        }

        (await CommentsOfAsync(ticket.Id.Value)).Should().Equal(["Inside"]);
        (await ReactionsOfAsync(ticket.Id.Value)).Should().Be(1);
    }

    [Fact]
    public async Task A_reaction_on_a_comment_follows_the_ticket_it_belongs_to()
    {
        database.Require();
        var ticket = await TicketAsync("Alice's, open for the north");
        var comment = await CommentOfAsync(ticket);

        var byCarol = () => database.RunAsAsync(Carol, AddReaction, Cancellation, Guid.NewGuid(), ticket, comment, PostgresRowAccessDatabase.Carol);
        var byBob = () => database.RunAsAsync(Bob, AddReaction, Cancellation, Guid.NewGuid(), ticket, comment, PostgresRowAccessDatabase.Bob);

        await RefusedAsync(byCarol, "Carol cannot even read the ticket");
        await RefusedAsync(byBob, "Bob reads the ticket, and may not change it");
        (await database.RunAsAsync(Alice, AddReaction, Cancellation, Guid.NewGuid(), ticket, comment, PostgresRowAccessDatabase.Alice)).Should().Be(1, "Alice owns the ticket the comment is on");

        (await ReactionsAsync(Alice, ticket)).Should().Be(1);
        (await ReactionsAsync(Bob, ticket)).Should().Be(1, "Bob reads the comment, so he reads its reactions");
        (await ReactionsAsync(Carol, ticket)).Should().Be(0);
        (await database.RunAsAsync(Bob, RemoveReactions, Cancellation, ticket)).Should().Be(0);
        (await database.RunAsAsync(Alice, RemoveReactions, Cancellation, ticket)).Should().Be(1);
    }

    [Fact]
    public async Task A_root_inserted_by_another_transaction_is_not_new()
    {
        database.Require();
        var own = new Ticket(TicketId.CreateSequential(), "Bob's first", owner: null, "north", TicketStatus.Open, isPublic: false);
        var someoneElses = new Ticket(TicketId.CreateSequential(), "Filed meanwhile", owner: null, "north", TicketStatus.Open, isPublic: false);

        await using var bob = database.CreateContext(Bob);
        await using var transaction = await bob.Database.BeginTransactionAsync(Cancellation);
        bob.Tickets.Add(own);
        await bob.SaveChangesAsync(Cancellation);
        (await bob.Database.ExecuteSqlRawAsync(AddComment, [Guid.NewGuid(), own.Id.Value, "On Bob's own, still new"], Cancellation)).Should().Be(1);

        // Filed, and committed, by a transaction that began after Bob's.
        await using (var meanwhile = database.CreateContext(Caller.System))
        {
            meanwhile.Tickets.Add(someoneElses);
            await meanwhile.SaveChangesAsync(Cancellation);
        }

        var onTheirs = () => bob.Database.ExecuteSqlRawAsync(AddComment, [Guid.NewGuid(), someoneElses.Id.Value, "On one Bob did not file"], Cancellation);

        await RefusedAsync(onTheirs, "Bob reads the ticket and may file one like it, but this transaction did not write it");
    }

    [Fact]
    public async Task A_create_only_caller_cannot_make_an_existing_root_new()
    {
        database.Require();

        // Filed for the north and owned by nobody, as Bob may file one himself: Bob's Create rule holds of it,
        // and he reads it while it is open, so only whether his transaction wrote it stands in his way.
        var ticket = await FileForTheNorthAsync("Filed for the north earlier");
        var version = await VersionOfAsync(ticket);

        await using (var bob = database.CreateContext(Bob))
        {
            await using var transaction = await bob.Database.BeginTransactionAsync(Cancellation);
            (await bob.Database.ExecuteSqlRawAsync(TouchTicket, [ticket], Cancellation))
                .Should().Be(0, "Bob may not change the ticket, so his update writes no new version of it");

            var add = () => bob.Database.ExecuteSqlRawAsync(AddComment, [Guid.NewGuid(), ticket, "Bob was here"], Cancellation);

            await RefusedAsync(add, "the ticket was written before Bob's transaction began, and his update did not write it again");
        }

        (await VersionOfAsync(ticket)).Should().Be(version, "nobody wrote the ticket since it was filed");

        // The control: the same insert once Bob's transaction did write the ticket, here as the tables'
        // owner, as a trigger or a function that runs as its owner would. The documentation says so.
        await using (var bob = database.CreateContext(Bob))
        {
            await using var transaction = await bob.Database.BeginTransactionAsync(Cancellation);
            await bob.Database.ExecuteSqlRawAsync("RESET ROLE", Cancellation);
            (await bob.Database.ExecuteSqlRawAsync(TouchTicket, [ticket], Cancellation)).Should().Be(1);
            await bob.Database.ExecuteSqlRawAsync("SET LOCAL ROLE authenticated", Cancellation);

            (await bob.Database.ExecuteSqlRawAsync(AddComment, [Guid.NewGuid(), ticket, "Now that it counts"], Cancellation))
                .Should().Be(1, "a root this transaction wrote counts as written, whoever wrote it");

            await transaction.RollbackAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_caller_who_may_create_a_root_but_not_read_it_cannot_add_its_entities()
    {
        database.Require();
        var ticket = Guid.NewGuid();

        await using var bob = database.CreateContext(Bob);
        await using var transaction = await bob.Database.BeginTransactionAsync(Cancellation);
        (await bob.Database.ExecuteSqlRawAsync(
                """INSERT INTO desk."Tickets" ("Id", "Title", "Owner", "Team", "Status", "IsPublic", "Version") VALUES ({0}, 'Closed at once', NULL, 'north', 1, false, 1)""",
                [ticket],
                Cancellation))
            .Should().Be(1, "Bob may file a ticket for the team, closed or not");

        var add = () => bob.Database.ExecuteSqlRawAsync(AddComment, [Guid.NewGuid(), ticket, "On a ticket Bob cannot read"], Cancellation);

        await RefusedAsync(add, "the policy reads the ticket as Bob, who reads only the team's open tickets");
    }

    /// <summary>Files a ticket for the north, owned by nobody, as the application itself, and returns its id.</summary>
    private async Task<Guid> FileForTheNorthAsync(string title)
    {
        var ticket = new Ticket(TicketId.CreateSequential(), title, owner: null, "north", TicketStatus.Open, isPublic: false);
        await using var context = database.CreateContext(Caller.System);
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync(Cancellation);
        return ticket.Id.Value;
    }

    /// <summary>The id of the transaction that wrote the ticket's current version.</summary>
    private async Task<string> VersionOfAsync(Guid ticket)
    {
        await using var context = database.CreateContext(Caller.System);
        return await context.Database.SqlQueryRaw<string>("""SELECT "xmin"::text AS "Value" FROM desk."Tickets" WHERE "Id" = {0}""", ticket).SingleAsync(Cancellation);
    }

    /// <summary>
    /// That <paramref name="write"/> is refused by a policy: SQLSTATE 42501, and Postgres's words for a row
    /// a policy does not allow, so a missing grant, which is 42501 as well, cannot pass for one.
    /// </summary>
    private static async Task RefusedAsync(Func<Task<int>> write, string because = "")
    {
        var refusal = (await write.Should().ThrowAsync<PostgresException>(because)).Which;
        refusal.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, because);
        refusal.MessageText.Should().Contain("row-level security", because);
    }

    private async Task<Guid> TicketAsync(string title)
    {
        await using var context = database.CreateContext(Caller.System);
        return await context.Tickets.Where(ticket => ticket.Title == title).Select(ticket => ticket.Id.Value).SingleAsync(Cancellation);
    }

    private async Task<Guid> CommentOfAsync(Guid ticket)
    {
        await using var context = database.CreateContext(Caller.System);
        return await context.Database.SqlQueryRaw<Guid>("""SELECT "Id" AS "Value" FROM desk."TicketComment" WHERE "TicketId" = {0}""", ticket).SingleAsync(Cancellation);
    }

    /// <summary>The texts of a ticket's comments, as the application itself reads them.</summary>
    private async Task<List<string>> CommentsOfAsync(Guid ticket)
    {
        await using var context = database.CreateContext(Caller.System);
        return await context.Database.SqlQueryRaw<string>("""SELECT "Text" AS "Value" FROM desk."TicketComment" WHERE "TicketId" = {0} ORDER BY "Text" """, ticket).ToListAsync(Cancellation);
    }

    /// <summary>How many reactions to a ticket's comments there are, as the application itself counts them.</summary>
    private Task<int> ReactionsOfAsync(Guid ticket) => ReactionsAsync(Caller.System, ticket);

    private async Task<int> ReactionsAsync(Caller caller, Guid ticket)
    {
        await using var context = database.CreateContext(caller);
        return await context.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM desk."CommentReaction" WHERE "TicketCommentTicketId" = {0}""", ticket).SingleAsync(Cancellation);
    }

    /// <summary>A table of the probe's own, so the desk's tables and policies stay out of it.</summary>
    private static async Task<string> ProbeTableAsync(NpgsqlConnection connection)
    {
        var table = "written_" + Guid.NewGuid().ToString("N");
        await RunAsync(connection, $"CREATE TABLE {table} (id integer PRIMARY KEY)");
        return table;
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    /// <summary>Each row of <paramref name="table"/>, with whether the transaction running on <paramref name="connection"/> wrote it.</summary>
    private static async Task<List<(int Id, bool Written)>> WrittenAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand($"SELECT id, ddd.written_in_this_transaction(xmin) FROM {table} ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        var rows = new List<(int, bool)>();
        while (await reader.ReadAsync(Cancellation))
        {
            rows.Add((reader.GetInt32(0), reader.GetBoolean(1)));
        }

        return rows;
    }
}
