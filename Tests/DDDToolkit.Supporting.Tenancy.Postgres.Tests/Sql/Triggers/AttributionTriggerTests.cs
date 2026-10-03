using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The columns that say who wrote a row first and who changed it last, held by the database: on a table whose
/// entity keeps them, a trigger holds a signed-in user to its own seat, keeps the application's own work from
/// writing a seat's kind, and lets neither change who wrote the row first. The save fills every one of them
/// itself, so the trigger only ever refuses a statement that goes around the model.
/// <para>
/// The tallies' module keeps them. Seth supervises North and Oli operates at North Pier: both keep tallies there.
/// </para>
/// </summary>
public abstract class AttributionTriggerTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static readonly Guid Operator = Guid.Parse("d0000000-0000-4000-8000-000000000077");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_forged_seat_is_refused_by_the_database()
    {
        var database = await TallyDatabase.CreateAsync(postgres, Cancellation, names);
        var tallies = TallyDatabase.NamesOf(database);
        string Insert(string name, SeatId? seat, string kind, Guid? identity = null)
            => $"INSERT INTO {tallies.Table} ({tallies.Columns}) VALUES ({TallyNames.Values(Harbor, North, name, seat, kind, identity)})";
        string Change(string name, string set)
            => $"UPDATE {tallies.Table} SET {set} WHERE {tallies.Column("Name")} = '{name}'";

        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            // A statement of the application's own that goes around the model: its own seat goes in, and nobody else's does.
            (await seth.ExecuteAsync(Insert("Crates", Seth.Seat, "seat"), Cancellation)).Should().Be(1);
            await RefusedAsync(seth, Insert("Barrels", Oli.Seat, "seat"), TenancyAttributionRefusals.OfASeat);
            await RefusedAsync(seth, Insert("Barrels", seat: null, "seat"), TenancyAttributionRefusals.OfASeat);
            await RefusedAsync(seth, Insert("Barrels", seat: null, "system"), TenancyAttributionRefusals.OfASeat);
            await RefusedAsync(seth, Insert("Barrels", seat: null, "operator", Operator), TenancyAttributionRefusals.OfASeat);
            await RefusedAsync(seth, Insert("Barrels", Seth.Seat, "seat", Operator), TenancyAttributionRefusals.OfASeat);

            // A change says who made it: Seth cannot write that Oli did.
            (await seth.ExecuteAsync(Change("Crates", $"{tallies.Column("Name")} = 'Crates, counted'"), Cancellation)).Should().Be(1, "who changed it last is Seth already");
            await RefusedAsync(seth, Change("Crates, counted", $"{tallies.Column(TenancyAttribution.ChangedBySeat)} = '{Oli.Seat.Value}'"), TenancyAttributionRefusals.OfASeat);
            await RefusedAsync(seth, Change("Crates, counted", $"{tallies.Column(TenancyAttribution.ChangedByKind)} = 'system'"), TenancyAttributionRefusals.OfASeat);
            await seth.CommitAsync(Cancellation);
        }

        // Nor can Oli change Seth's tally and leave Seth's name on the change.
        await TenancySeed.HoldAtAsync(database, Oli, North, [TestHost.HostCatalogue.WidgetChange], Cancellation);
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(oli, Change("Crates, counted", $"{tallies.Column("Name")} = 'Crates, recounted'"), TenancyAttributionRefusals.OfASeat);
            (await oli.ExecuteAsync(
                Change("Crates, counted", $"{tallies.Column("Name")} = 'Crates, recounted', {tallies.Column(TenancyAttribution.ChangedBySeat)} = '{Oli.Seat.Value}'"),
                Cancellation)).Should().Be(1);
        }

        // The application's own work is recorded as itself, an operator or a token, never as a seat.
        await using (var work = await AsCaller.SystemInAsync(database, Harbor, TallyContext.Schema, Cancellation))
        {
            await RefusedAsync(work, Insert("Pallets", Seth.Seat, "seat"), TenancyAttributionRefusals.OfSystemWork);
            (await work.ExecuteAsync(Insert("Pallets", seat: null, "system"), Cancellation)).Should().Be(1);
            (await work.ExecuteAsync(Insert("Drums", seat: null, "operator", Operator), Cancellation)).Should().Be(1);
            await RefusedAsync(work, Change("Pallets", $"{tallies.Column(TenancyAttribution.ChangedByKind)} = 'seat', {tallies.Column(TenancyAttribution.ChangedBySeat)} = '{Seth.Seat.Value}'"), TenancyAttributionRefusals.OfSystemWork);
        }
    }

    [Fact]
    public async Task The_first_writer_cannot_be_rewritten()
    {
        var database = await TallyDatabase.CreateAsync(postgres, Cancellation, names);
        var tallies = TallyDatabase.NamesOf(database);
        await TenancySeed.HoldAtAsync(database, Oli, North, [TestHost.HostCatalogue.WidgetChange], Cancellation);

        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            await seth.ExecuteAsync($"INSERT INTO {tallies.Table} ({tallies.Columns}) VALUES ({TallyNames.Values(Harbor, North, "Crates", Seth.Seat, "seat")})", Cancellation);
            await seth.CommitAsync(Cancellation);
        }

        var asOli = $"{tallies.Column(TenancyAttribution.ChangedBySeat)} = '{Oli.Seat.Value}'";
        string Rewrite(string set) => $"UPDATE {tallies.Table} SET {set} WHERE {tallies.Column("Name")} = 'Crates'";

        // Oli changes the tally as himself, and cannot make himself the one who wrote it.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(oli, Rewrite($"{asOli}, {tallies.Column(TenancyAttribution.CreatedBySeat)} = '{Oli.Seat.Value}'"), TenancyAttributionRefusals.IsKept);
            await RefusedAsync(oli, Rewrite($"{asOli}, {tallies.Column(TenancyAttribution.CreatedByKind)} = 'system', {tallies.Column(TenancyAttribution.CreatedBySeat)} = NULL"), TenancyAttributionRefusals.IsKept);
            (await oli.ExecuteAsync(Rewrite(asOli), Cancellation)).Should().Be(1);
        }

        // Nor can the application's own work.
        await using (var work = await AsCaller.SystemInAsync(database, Harbor, TallyContext.Schema, Cancellation))
        {
            await RefusedAsync(
                work,
                Rewrite($"{tallies.Column(TenancyAttribution.ChangedByKind)} = 'system', {tallies.Column(TenancyAttribution.ChangedBySeat)} = NULL, {tallies.Column(TenancyAttribution.CreatedByIdentity)} = '{Operator}'"),
                TenancyAttributionRefusals.IsKept);
        }

        // The tables' owner is no caller, and is not held: it fills the columns of rows that were there before them.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ExecuteAsync(Rewrite($"{tallies.Column(TenancyAttribution.CreatedBySeat)} = '{Oli.Seat.Value}'"), Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task An_insert_with_attribution_needs_no_returning()
    {
        var database = await TallyDatabase.CreateAsync(postgres, Cancellation, names);
        var tallies = TallyDatabase.NamesOf(database);
        await TenancySeed.HoldAtAsync(database, Oli, North, [TestHost.HostCatalogue.WidgetChange], Cancellation);
        var recorder = new CommandRecorder();
        await using var services = TallyDatabase.Services(database, contexts: options => options.AddInterceptors(recorder));

        // Through the model, as a seat, under the policies and the trigger: the save fills the columns itself.
        var crates = TallyId.CreateSequential();
        recorder.Clear();
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, async scoped =>
        {
            scoped.Tallies().Tallies.Add(new Tally(crates, Harbor, North, "Crates"));
            await scoped.Tallies().SaveChangesAsync(Cancellation);
        });

        var insert = recorder.Sent.Should().ContainSingle(command => command.Text.Contains("INSERT INTO", StringComparison.Ordinal)).Which.Text;
        insert.Should().NotContain("RETURNING", "every value of the row is the application's own, so the insert reads nothing back and asks no privilege to");
        insert.Should().Contain(tallies.Column(TenancyAttribution.CreatedBySeat).Trim('"')).And.Contain(tallies.Column(TenancyAttribution.ChangedByKind).Trim('"'));

        // A change by another seat, and one carried out for an operator, pass the trigger as they are saved.
        await services.BySeat(Oli.Identity, Harbor, Oli.Seat, async scoped =>
        {
            (await scoped.Tallies().Tallies.SingleAsync(tally => tally.Id == crates, Cancellation)).Rename("Crates, counted");
            await scoped.Tallies().SaveChangesAsync(Cancellation);
        });
        (await RecordedAsync(database, tallies, "Crates, counted")).Should().Be(($"{Seth.Seat.Value}", "seat", null, $"{Oli.Seat.Value}", "seat", null));

        await using (var scope = services.Scope())
        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(Harbor, Operator, scope: TallyContext.Schema))
        {
            var context = scope.ServiceProvider.Tallies();
            (await context.Tallies.SingleAsync(tally => tally.Id == crates, Cancellation)).Rename("Crates, checked");
            context.Tallies.Add(new Tally(TallyId.CreateSequential(), Harbor, South, "Drums"));
            await context.SaveChangesAsync(Cancellation);
        }

        (await RecordedAsync(database, tallies, "Crates, checked")).Should().Be(($"{Seth.Seat.Value}", "seat", null, null, "operator", $"{Operator}"));
        (await RecordedAsync(database, tallies, "Drums")).Should().Be((null, "operator", $"{Operator}", null, "operator", $"{Operator}"));
    }

    [Fact]
    public void The_trigger_is_written_for_the_tables_that_keep_who_changed_a_row_and_no_others()
    {
        var scripts = TallyDatabase.Scripts(names);
        using var model = TallyContext.ForModel(names);
        var tally = model.Model.FindEntityType(typeof(Tally))!;
        var tallies = TallyDatabase.NamesOf(new TestDatabase("", "", names));

        // Once, in the tallies' script: the function in the tallies' own schema, and the trigger with the six
        // columns as the model names them, whatever a naming convention made of them.
        var written = scripts.Should().ContainSingle(script => script.Contains("attribution_matches_caller", StringComparison.Ordinal)).Which;
        written.Should().Contain("CREATE OR REPLACE FUNCTION \"tallies\".attribution_matches_caller() RETURNS trigger\n    LANGUAGE plpgsql SET search_path = '' AS $body$\n")
            .And.NotContain("attribution_matches_caller() RETURNS trigger\n    LANGUAGE plpgsql SECURITY DEFINER", "it runs as the caller, whose role it reads");
        var columns = string.Join(", ", TenancyAttribution.Columns.Select(column => "'" + tally.FindProperty(column)!.GetColumnName() + "'"));
        written.Should().Contain(
            $"CREATE TRIGGER tenancy_attribution_matches_caller BEFORE INSERT OR UPDATE ON {tallies.Table}\n" +
            $"    FOR EACH ROW EXECUTE FUNCTION \"tallies\".attribution_matches_caller({columns});\n");
        written.Should().Contain("IF CURRENT_USER = 'authenticated' THEN").And.Contain("ELSIF CURRENT_USER = 'ddd_system_in' THEN");

        // A host that calls its callers' roles otherwise gets the trigger for those roles.
        var renamed = TenancyPostgres.AccessScripts(
            rules: [.. WidgetRules.All, TallyRules.Kept],
            names: names,
            roles: new DDDToolkit.EntityFramework.Postgres.RowAccessRoleNames("members", "visitors", "workers"),
            more: [model]);
        string.Concat(renamed).Should().Contain("IF CURRENT_USER = 'members' THEN").And.Contain("ELSIF CURRENT_USER = 'workers' THEN");

        // Tenancy's own context and the widgets' keep no such columns, and get no trigger.
        string.Concat(TenancyPostgres.AccessScripts(names: names)).Should().NotContain("attribution_matches_caller");
    }

    /// <summary>The six columns of the tally called <paramref name="name"/>, as text, read by the tables' owner.</summary>
    private static async Task<(string? CreatedBySeat, string CreatedByKind, string? CreatedByIdentity, string? ChangedBySeat, string ChangedByKind, string? ChangedByIdentity)> RecordedAsync(
        TestDatabase database, TallyNames tallies, string name)
    {
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        var columns = string.Join(" || '|' || ", TenancyAttribution.Columns.Select(column => $"coalesce({tallies.Column(column)}::text, '')"));
        var parts = (await owner.ScalarAsync<string>($"SELECT {columns} FROM {tallies.Table} WHERE {tallies.Column("Name")} = $1", Cancellation, name)).Split('|');
        string? Value(int index) => parts[index].Length == 0 ? null : parts[index];

        return (Value(0), parts[1], Value(2), Value(3), parts[4], Value(5));
    }

    private static async Task RefusedAsync(AsCaller caller, string sql, string message)
    {
        var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(sql, Cancellation)).Should().ThrowAsync<PostgresException>(sql);
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, sql);
        refusal.Which.MessageText.Should().Be(message, sql);
    }
}

/// <summary>What the trigger tells a statement it refuses.</summary>
internal static class TenancyAttributionRefusals
{
    public const string OfASeat = "A seat records a row as written and changed by itself.";

    public const string OfSystemWork = "System work records a row as written and changed by no seat.";

    public const string IsKept = "Who wrote a row first does not change.";
}

/// <summary>Who changed a row, held by the database, under the names Entity Framework gives the tables and columns.</summary>
public sealed class AttributionTriggerTestsOnDefaultNames(TenancyPostgres postgres) : AttributionTriggerTests(postgres, TenancyNaming.Default);

/// <summary>Who changed a row, held by the database, under snake_case names.</summary>
public sealed class AttributionTriggerTestsOnSnakeCase(TenancyPostgres postgres) : AttributionTriggerTests(postgres, TenancyNaming.SnakeCase);
