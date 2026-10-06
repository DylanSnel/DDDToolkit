using System.Text.Json;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Tenancy's access history under the policies. A row is added by the save that raised its event: by a person
/// about their own seat, in the tenant the connection names, and by Tenancy's own system work in its tenant. It is
/// read by a seat that holds <c>tenancy.history.view</c> for the whole tenant, and by system work in the tenant;
/// never across tenants, and it changes for nobody. What is kept is each event that changes access, with who made
/// the change in the row and in the event.
/// <para>
/// The seeded data was written through the use cases, so each tenant has its history already: provisioned,
/// seated and granted by system work.
/// </para>
/// </summary>
public abstract class AccessHistoryPolicyTests(TenancyPostgres postgres, TenancyNaming names)
{
    private const string Columns = "\"Id\", \"EventName\", \"Version\", \"Payload\", \"OccurredAt\", \"RecordedAt\", \"ActedByKind\", \"ActedById\", \"TenantId\"";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A row somebody would add past the model: an event of <paramref name="tenant"/>, said to be the work of one actor.</summary>
    private static string Insert(TenantId tenant, string kind, string? actor)
        => $"INSERT INTO ddd.\"EventLog\" ({Columns}) VALUES (gen_random_uuid(), 'tenancy.seat-suspended', 1, '{{}}', now(), now(), '{kind}', {(actor is null ? "NULL" : $"'{actor}'")}, {tenant.Value})";

    [Fact]
    public async Task A_seat_without_the_history_key_reads_no_history()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Seth supervises North: units, seats, grants and widgets, and not the history.
        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await seth.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\"", Cancellation)).Should().Be(0);
        }

        (await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Tenancy().Set<EventLogEntry>().CountAsync(Cancellation))).Should().Be(0);

        // Held below the root, the key is not held for the whole tenant: still nothing.
        await TenancySeed.HoldAtAsync(database, Seth, North, [TenancyKeys.HistoryView], Cancellation);
        (await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Tenancy().Set<EventLogEntry>().CountAsync(Cancellation))).Should().Be(0);

        // An anonymous caller reads none either, and a person in a tenant where they have no seat.
        await using (var anonymous = await AsCaller.AnonymousAsync(database, Cancellation))
        {
            (await anonymous.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\"", Cancellation)).Should().Be(0);
        }

        await using var elsewhere = await AsCaller.PersonAsync(database, Ada.Identity, Orchard, Cancellation);
        (await elsewhere.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\"", Cancellation)).Should().Be(0, "Ada administers Harbor, and has no seat in Orchard");
    }

    [Fact]
    public async Task A_history_reader_reads_only_its_own_tenant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        long ofHarbor, ofOrchard;
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            ofHarbor = await owner.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\" WHERE \"TenantId\" = 1", Cancellation);
            ofOrchard = await owner.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\" WHERE \"TenantId\" = 2", Cancellation);
            ofHarbor.Should().BeGreaterThan(ofOrchard).And.BeGreaterThan(10, "Harbor was provisioned, and its units, seats and grants made");
            ofOrchard.Should().BeGreaterThan(0);
        }

        // An administrator holds every live key, the history's among them, at the root.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\"", Cancellation)).Should().Be(ofHarbor);
            (await ada.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\" WHERE \"TenantId\" <> 1", Cancellation)).Should().Be(0, "no row of another tenant");
            (await ada.ListAsync<string>("SELECT DISTINCT \"ActedByKind\" FROM ddd.\"EventLog\"", Cancellation)).Should().Equal("system");
        }

        await using (var odette = await AsCaller.PersonAsync(database, Odette.Identity, Orchard, Cancellation))
        {
            (await odette.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\"", Cancellation)).Should().Be(ofOrchard);
        }

        // The key alone is enough, given for the whole tenant: reading what happened manages no access, so the
        // role that holds it is given like any other.
        await TenancySeed.HoldAtAsync(database, Hiro, HarborRoot, [TenancyKeys.HistoryView], Cancellation);
        var read = await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Tenancy().Set<EventLogEntry>()
            .Select(entry => EF.Property<TenantId>(entry, EntityFramework.TenancyEventLogTable.TenantId))
            .ToListAsync(Cancellation));
        read.Should().HaveCountGreaterThan((int)ofHarbor, "what was just done for Hiro is in the history too").And.OnlyContain(tenant => tenant == Harbor);

        // System work reads its tenant's history in any scope, and none without a tenant.
        await using (var work = await AsCaller.SystemInAsync(database, Orchard, "widgets", Cancellation))
        {
            (await work.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\"", Cancellation)).Should().Be(ofOrchard);
        }

        await using var nowhere = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation);
        (await nowhere.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\"", Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task A_change_of_access_is_kept_with_who_made_it_and_a_rename_is_not()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        var before = await HistoryOfHarborAsync(services);

        // Ada gives Oli a role, puts a key into a role and renames a unit: three saves by a person, each through the
        // policies on Tenancy's tables and on the history.
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().GrantAsync(Oli.Seat, NorthPier, HarborRoles.Watcher, until: null, reason: null, Cancellation));
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Roles().SetKeysAsync(GrantsDesk, [TenancyKeys.GrantsManage, TenancyKeys.SeatsManage], Cancellation));
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Organization().RenameUnitAsync(South, "Southside", Cancellation));

        var added = (await HistoryOfHarborAsync(services)).ExceptBy(before.Select(row => row.Id), row => row.Id).ToList();
        added.Select(row => row.EventName).Should().BeEquivalentTo(
            ["tenancy.organization-role-granted", "tenancy.role-keys-changed"],
            "the rename changes nobody's access, and is no part of the history");

        // The row and the event say the same about who did it: Ada's seat.
        foreach (var row in added)
        {
            (row.ActedByKind, row.ActedById).Should().Be(("seat", Ada.Seat.Value.ToString("D")), row.EventName);
            using var payload = JsonDocument.Parse(row.Payload);
            var by = payload.RootElement.GetProperty("By");
            by.GetProperty("Kind").GetString().Should().Be("seat", row.EventName);
            by.GetProperty("Seat").GetGuid().Should().Be(Ada.Seat.Value, row.EventName);
        }

        using (var changed = JsonDocument.Parse(added.Single(row => row.EventName == "tenancy.role-keys-changed").Payload))
        {
            changed.RootElement.GetProperty("Added").EnumerateArray().Select(key => key.GetString()).Should().Equal(TenancyKeys.SeatsManage);
            changed.RootElement.GetProperty("Removed").GetArrayLength().Should().Be(0);
        }

        // An operator has Orchard suspended. System work carries it out, in Tenancy's scope, and the row and the event
        // name the operator, by identity and with no seat.
        var operatorIdentity = Guid.NewGuid();
        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(Orchard, operatorIdentity))
        {
            await services.InScopeAsync(scoped => scoped.Tenants().SuspendAsync("Asked for by the owner", Cancellation));
        }

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM ddd.\"OutboxMessages\" WHERE \"EventName\" = 'tenancy.organization-unit-renamed'", Cancellation))
            .Should().Be(1, "the rename is stored with the other events, for whoever handles it");
        (await owner.ScalarAsync<string>(
            "SELECT \"ActedByKind\" || ' ' || \"ActedById\" FROM ddd.\"EventLog\" WHERE \"EventName\" = 'tenancy.tenant-suspended' AND \"TenantId\" = 2",
            Cancellation)).Should().Be($"operator {operatorIdentity:D}");

        using var suspended = JsonDocument.Parse(await owner.ScalarAsync<string>(
            "SELECT \"Payload\" FROM ddd.\"EventLog\" WHERE \"EventName\" = 'tenancy.tenant-suspended' AND \"TenantId\" = 2",
            Cancellation));
        var byOperator = suspended.RootElement.GetProperty("By");
        byOperator.GetProperty("Kind").GetString().Should().Be("operator");
        byOperator.GetProperty("Operator").GetGuid().Should().Be(operatorIdentity);
        byOperator.GetProperty("Seat").ValueKind.Should().Be(JsonValueKind.Null, "an operator holds no seat");
        byOperator.GetProperty("Scope").GetString().Should().Be(TenancyWork.SystemScope);
    }

    [Fact]
    public async Task A_seat_cannot_log_as_another_seat()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            // A statement that goes around the model: what Seth writes into the history is about Seth's seat, in Harbor.
            (await seth.ExecuteAsync(Insert(Harbor, "seat", $"{Seth.Seat.Value}"), Cancellation)).Should().Be(1);
            await RefusedAsync(seth, Insert(Harbor, "seat", $"{Oli.Seat.Value}"));
            await RefusedAsync(seth, Insert(Harbor, "seat", actor: null));
            await RefusedAsync(seth, Insert(Harbor, "system", TenancyWork.SystemScope));
            await RefusedAsync(seth, Insert(Harbor, "operator", $"{Seth.Identity}"));
            await RefusedAsync(seth, Insert(Orchard, "seat", $"{Seth.Seat.Value}"));
        }

        // Oli has a seat in each tenant: in Orchard he writes as his seat there, and not as his seat in Harbor.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Orchard, Cancellation))
        {
            (await oli.ExecuteAsync(Insert(Orchard, "seat", $"{OliInOrchard.Value}"), Cancellation)).Should().Be(1);
            await RefusedAsync(oli, Insert(Orchard, "seat", $"{Oli.Seat.Value}"));
            await RefusedAsync(oli, Insert(Harbor, "seat", $"{Oli.Seat.Value}"));
        }

        // System work adds to its own tenant's history in Tenancy's scope, and in no other scope and no other tenant.
        await using (var tenancy = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await tenancy.ExecuteAsync(Insert(Harbor, "system", TenancyWork.SystemScope), Cancellation)).Should().Be(1);
            (await tenancy.ExecuteAsync(Insert(Harbor, "operator", $"{Guid.NewGuid()}"), Cancellation)).Should().Be(1, "an operator's act is carried out, and recorded, by system work");
            await RefusedAsync(tenancy, Insert(Orchard, "system", TenancyWork.SystemScope));
        }

        await using (var module = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation))
        {
            await RefusedAsync(module, Insert(Harbor, "system", "widgets"));
        }

        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        await RefusedAsync(anonymous, Insert(Harbor, "anonymous", actor: null));
    }

    [Fact]
    public async Task System_work_cannot_log_as_a_seat()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        long before;
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            before = await owner.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\" WHERE \"ActedByKind\" = 'seat'", Cancellation);
        }

        // A statement that goes around the model, as Tenancy's own work in Harbor: a row of the history stays for
        // good, so the scoped system role says it was the system, an operator or a token, and never a seat, which
        // only the person in that seat says.
        await using (var tenancy = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            await RefusedAsync(tenancy, Insert(Harbor, "seat", $"{Seth.Seat.Value}"));
            await RefusedAsync(tenancy, Insert(Harbor, "seat", actor: null));
            await RefusedAsync(tenancy, Insert(Harbor, "user", $"{Seth.Identity}"));
            await RefusedAsync(tenancy, Insert(Harbor, "Seat", $"{Seth.Seat.Value}"));

            (await tenancy.ExecuteAsync(Insert(Harbor, "system", TenancyWork.SystemScope), Cancellation)).Should().Be(1);
            (await tenancy.ExecuteAsync(Insert(Harbor, "operator", $"{Guid.NewGuid()}"), Cancellation)).Should().Be(1);
            (await tenancy.ExecuteAsync(Insert(Harbor, "token", $"{Seth.Seat.Value}"), Cancellation)).Should().Be(1, "a link's token is carried out by system work, for the seat it stands for");
        }

        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ScalarAsync<long>("SELECT count(*) FROM ddd.\"EventLog\" WHERE \"ActedByKind\" = 'seat'", Cancellation)).Should().Be(before, "no row says a seat did what system work did");
    }

    [Fact]
    public async Task A_seat_that_stops_itself_is_still_recorded()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Hiro administers Harbor next to Ada, and deactivates his own seat: once that row is written he is no seat
        // to be found by, and the same save still says who did it.
        await TenancySeed.HoldAtAsync(database, Hiro, HarborRoot, keys: null, Cancellation);
        await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().DeactivateAsync(Hiro.Seat, Cancellation));

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>(
                "SELECT \"ActedByKind\" || ' ' || \"ActedById\" FROM ddd.\"EventLog\" WHERE \"EventName\" = 'tenancy.seat-deactivated' AND \"TenantId\" = 1",
                Cancellation)).Should().Equal($"seat {Hiro.Seat.Value}");
            (await owner.ScalarAsync<string>("SELECT \"Status\" FROM tenancy.\"Seats\" WHERE \"Id\" = $1", Cancellation, Hiro.Seat.Value)).Should().Be(names.Stored(SeatStatus.Deactivated));
        }

        // That save was the last he records. A seat that is not in use is no calling seat, and its row counts only
        // in the transaction that wrote it: afterwards the person adds nothing to the tenant's history.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(hiro, Insert(Harbor, "seat", $"{Hiro.Seat.Value}"));
        }

        // Nor does a person whose seat somebody else suspended, or one whose tenant is suspended.
        await using (var sue = await AsCaller.PersonAsync(database, Sue.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(sue, Insert(Harbor, "seat", $"{Sue.Seat.Value}"));
        }

        await using var quin = await AsCaller.PersonAsync(database, Quin.Identity, Quay, Cancellation);
        await RefusedAsync(quin, Insert(Quay, "seat", $"{Quin.Seat.Value}"));
    }

    [Fact]
    public async Task A_row_of_the_history_changes_for_nobody()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // No policy lets a caller change or remove a row: an administrator's statement finds none to change.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.AttemptAsync("UPDATE ddd.\"EventLog\" SET \"EventName\" = 'tenancy.nothing-happened'", Cancellation)).Should().Be(0);
            (await ada.AttemptAsync("DELETE FROM ddd.\"EventLog\"", Cancellation)).Should().Be(0);
        }

        await using (var tenancy = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await tenancy.AttemptAsync("UPDATE ddd.\"EventLog\" SET \"ActedByKind\" = 'seat'", Cancellation)).Should().Be(0);
            (await tenancy.AttemptAsync("DELETE FROM ddd.\"EventLog\"", Cancellation)).Should().Be(0);
        }

        // And the tables' owner, whom no policy holds, is refused by the table's guard: the history only grows.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        foreach (var statement in new[] { "UPDATE ddd.\"EventLog\" SET \"ActedByKind\" = 'seat'", "DELETE FROM ddd.\"EventLog\"", "TRUNCATE ddd.\"EventLog\"" })
        {
            var refusal = await FluentActions.Awaiting(() => owner.AttemptAsync(statement, Cancellation)).Should().ThrowAsync<PostgresException>(statement);
            refusal.Which.SqlState.Should().Be("55000", statement);
        }
    }

    [Fact]
    public async Task The_start_up_check_names_a_history_without_row_level_security()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // The history is secured with Tenancy's tables, and checked with them: left open, every caller with
        // privileges on it would read every tenant's.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, names.Sql("ALTER TABLE ddd.\"EventLog\" DISABLE ROW LEVEL SECURITY"), Cancellation);

        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Row level security is off on Tenancy's tables ddd.{names.Shown("EventLog")}, so nothing holds a seat to its tenant there.*");
    }

    /// <summary>Harbor's history, as Ada reads it: its administrator holds the key that reads it.</summary>
    private static Task<List<Kept>> HistoryOfHarborAsync(TenancyServices services)
        => services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Tenancy().Set<EventLogEntry>()
            .Select(entry => new Kept(entry.Id, entry.EventName, entry.ActedByKind, entry.ActedById, entry.Payload))
            .ToListAsync(Cancellation));

    /// <summary>A row of the history, as a test reads it.</summary>
    private sealed record Kept(Guid Id, string EventName, string ActedByKind, string? ActedById, string Payload);

    private static async Task RefusedAsync(AsCaller caller, string sql)
    {
        var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(sql, Cancellation)).Should().ThrowAsync<PostgresException>(sql);
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, sql);
    }
}

/// <summary>The access history under the policies, under the names Entity Framework gives the tables and columns.</summary>
public sealed class AccessHistoryPolicyTestsOnDefaultNames(TenancyPostgres postgres) : AccessHistoryPolicyTests(postgres, TenancyNaming.Default);

/// <summary>The access history under the policies, under snake_case names with enums stored as snake_case text.</summary>
public sealed class AccessHistoryPolicyTestsOnSnakeCase(TenancyPostgres postgres) : AccessHistoryPolicyTests(postgres, TenancyNaming.SnakeCase);
