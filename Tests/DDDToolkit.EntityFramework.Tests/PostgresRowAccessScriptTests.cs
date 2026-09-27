using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The declarative half of the policies, against a real Postgres: a script says what the rules are now,
/// so running a later one takes out what an earlier one made and nothing else, and the drop at the start
/// of a module's migration is what lets that migration change a column a rule reads.
/// </summary>
/// <remarks>
/// A fixture of its own, because these tests change the policies and the schema the reading tests rely on.
/// </remarks>
public sealed class PostgresRowAccessScriptTests(PostgresRowAccessDatabase database) : IClassFixture<PostgresRowAccessDatabase>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rule_taken_out_loses_its_policy_and_a_policy_written_by_hand_stays()
    {
        database.Require();
        await database.RunAsOwnerAsync("""CREATE POLICY "Written by hand" ON desk."Tickets" FOR SELECT TO authenticated USING (false);""", Cancellation);

        await using var model = DeskContext.Create();
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.Owners]), Cancellation);

        (await database.PolicyNamesAsync("Tickets", Cancellation)).Should().Equal("Owners have their tickets", "Written by hand");
        (await database.PolicyNamesAsync("TicketComment", Cancellation)).Should().ContainSingle()
            .Which.Should().Be("TicketComment goes with its Tickets", "the aggregate's entities still follow it");
    }

    [Fact]
    public async Task The_drop_at_the_start_of_a_migration_is_what_lets_it_drop_a_column_a_rule_reads()
    {
        database.Require();
        await using var model = DeskContext.Create();
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Teammates, DeskRules.Public]), Cancellation);

        var alone = () => database.RunAsOwnerAsync("""ALTER TABLE desk."Tickets" DROP COLUMN "Team";""", Cancellation);
        (await alone.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.DependentObjectsStillExist, "\"Teammates read open tickets\" reads the column");

        var withTheDrop = () => database.RunAsOwnerAsync(PostgresRowAccess.DropStatement(model) + """ALTER TABLE desk."Tickets" DROP COLUMN "Team";""", Cancellation);
        await withTheDrop.Should().NotThrowAsync();
    }
}
