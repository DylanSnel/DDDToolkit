using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The help desk's rules as policies on a Postgres that is not Supabase's: no <c>auth</c> schema, no
/// PostgREST roles until <see cref="PostgresRowAccess.SetupScript"/> makes them, and the policies asking
/// <c>ddd.caller_id()</c>. What the database lets each caller see is what the rules say in C#.
/// </summary>
public sealed class PostgresRowAccessTests(PostgresRowAccessDatabase database) : IClassFixture<PostgresRowAccessDatabase>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Caller Alice = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Alice, team: "north"));

    private static readonly Caller Bob = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Bob, team: "north"));

    private static readonly Caller Carol = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Carol, team: "south"));

    public static TheoryData<string> CallerNames => ["Alice", "Bob", "Carol", "anonymous"];

    private static Caller Named(string name) => name switch
    {
        "Alice" => Alice,
        "Bob" => Bob,
        "Carol" => Carol,
        _ => Caller.Anonymous,
    };

    [Theory]
    [MemberData(nameof(CallerNames))]
    public async Task The_database_lets_each_caller_read_what_the_rules_say_in_csharp(string name)
    {
        database.Require();
        var caller = Named(name);

        var all = await database.TicketsAsync(Caller.System, Cancellation);
        var seen = await database.TicketsAsync(caller, Cancellation);

        all.Should().HaveCount(PostgresRowAccessDatabase.TicketCount, "the application itself is not subject to the rules");
        seen.Select(ticket => ticket.Title).Should().BeEquivalentTo(
            all.Where(ticket => DeskRules.Reads(ticket, caller)).Select(ticket => ticket.Title),
            "Postgres answers what OwnersHaveTheirTickets, TeammatesReadOpenTickets and PublicTicketsAreEveryones answer");
    }

    [Fact]
    public async Task A_teammate_reads_an_open_ticket_of_the_team_and_nothing_else_of_it()
    {
        database.Require();

        var seen = await database.TicketsAsync(Bob, Cancellation);

        seen.Select(ticket => ticket.Title).Should().BeEquivalentTo(["Bob's own, closed", "Alice's, open for the north", "Everyone's"]);
    }

    [Fact]
    public async Task Somebody_who_has_not_signed_in_reads_the_public_tickets_only()
    {
        database.Require();

        var seen = await database.TicketsAsync(Caller.Anonymous, Cancellation);

        seen.Should().ContainSingle().Which.Title.Should().Be("Everyone's");
    }

    [Fact]
    public async Task A_user_made_in_code_without_a_token_is_who_the_database_sees()
    {
        database.Require();

        var seen = await database.TicketsAsync(Caller.User(PostgresRowAccessDatabase.Carol), Cancellation);

        seen.Select(ticket => ticket.Title).Should().BeEquivalentTo(["Carol's own", "Everyone's", "Nobody's"], "the interceptor gives the database her id and role as the claims");
    }

    [Fact]
    public async Task A_watcher_reads_the_ticket_through_the_access_function_that_reads_the_tickets_entities()
    {
        database.Require();

        var carols = await database.TicketsAsync(Carol, Cancellation);
        var bobs = await database.TicketsAsync(Bob, Cancellation);

        carols.Select(ticket => ticket.Title).Should().Contain("Nobody's", "Carol watches it");
        bobs.Select(ticket => ticket.Title).Should().NotContain("Nobody's", "nobody else does");
        (await database.FunctionsAsync("is_watcher", Cancellation)).Should().ContainSingle()
            .Which.Should().Be(("desk.is_watcher(uuid)", true), "it is made once, and runs as its owner, so the watchers' table answers without asking the tickets' policies back");
    }

    [Fact]
    public async Task The_comments_follow_their_ticket()
    {
        database.Require();

        var comments = await database.CommentsAsync(Carol, Cancellation);

        comments.Should().BeEquivalentTo(["On Carol's own", "On everyone's", "On nobody's"], "Carol sees her own ticket, the public one and the one she watches, and the comments of nothing else");
    }

    [Fact]
    public async Task A_ticket_written_in_somebody_elses_name_is_refused()
    {
        database.Require();

        await using var context = database.CreateContext(Alice);
        context.Tickets.Add(new Ticket(TicketId.CreateSequential(), "Bob's, says Alice", PostgresRowAccessDatabase.Bob, team: null, TicketStatus.Open, isPublic: false));

        var save = () => context.SaveChangesAsync(Cancellation);

        (await save.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "new row violates row-level security policy");
    }

    [Fact]
    public async Task The_setup_can_run_again()
    {
        database.Require();

        var again = () => database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: PostgresRowAccessDatabase.LoginRole), Cancellation);

        await again.Should().NotThrowAsync();
    }
}
