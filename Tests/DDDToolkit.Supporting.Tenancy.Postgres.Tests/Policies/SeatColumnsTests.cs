using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// A seat's row, with every policy forced on the tables' owner. The policy lets a seat change its own row, and a seat
/// that manages seats or grants anywhere in the tenant change any seat's row, because every save of a seat writes its
/// version: a placement and a grant as much as a status. Tenancy guards its own columns of the row, and no others: the
/// id, the identity and the tenant change for no seat, whatever it manages, and the status only as Tenancy's use cases
/// change it. The application's own fields on its seat class are as writable as the row is, until a rule of the
/// application's own holds one: here a column rule on the job title, and none on the name.
/// </summary>
public sealed class SeatColumnsTests(TenancyPostgres postgres)
{
    /// <summary>The trigger that keeps what a seat is.</summary>
    private const string SeatIsFixed = "tenancy_seat_identity_is_fixed";

    /// <summary>The trigger on a seat's status.</summary>
    private const string StatusIsManaged = "tenancy_seat_status_is_managed";

    /// <summary>The trigger the export writes for <see cref="JobTitlesChangeWithTheSeatsKeyForTheWholeTenant"/>.</summary>
    private const string JobTitleRule = "seats_jobtitle_column_rule";

    /// <summary>The application's column rule on its own field, as the export is handed it.</summary>
    private static readonly RowAccessRule JobTitles = RowAccessRule.ForColumns<HostSeat>(
        "Job titles change with the seats key for the whole tenant",
        [nameof(HostSeat.JobTitle)],
        JobTitlesChangeWithTheSeatsKeyForTheWholeTenant.RowAccessSql,
        RowAccessRoles.User);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_seat_changes_a_seats_id_identity_or_tenant_whatever_it_manages_and_a_save_that_tries_is_access_refused()
    {
        var database = await ForcedAsync();
        string[] changes =
        [
            "UPDATE tenancy.\"Seats\" SET \"Identity\" = gen_random_uuid() WHERE \"Id\" = $1",
            "UPDATE tenancy.\"Seats\" SET \"TenantId\" = 2 WHERE \"Id\" = $1",
            "UPDATE tenancy.\"Seats\" SET \"Id\" = gen_random_uuid() WHERE \"Id\" = $1",
        ];

        // Each of them the policy lets change Oli's row: Ada holds every key, Seth manages seats at North, Hiro gives
        // roles there, and the row is Oli's own. Handing his seat to another account, moving it to Orchard or giving it
        // another id is none of theirs.
        (Person Caller, string Who)[] callers = [(Ada, "the administrator"), (Seth, "a seats manager"), (Hiro, "a grants manager"), (Oli, "the seat itself")];
        foreach (var change in changes)
        {
            foreach (var (caller, who) in callers)
            {
                await using var asCaller = await AsCaller.PersonAsync(database, caller.Identity, Harbor, Cancellation);
                var refused = (await FluentActions.Awaiting(() => asCaller.ExecuteAsync(change, Cancellation, Oli.Seat.Value)).Should().ThrowAsync<PostgresException>(change)).Which;
                (refused.SqlState, refused.Hint, refused.ConstraintName).Should().Be((PostgresErrorCodes.InsufficientPrivilege, "ddd:access.refused", SeatIsFixed), "{0} may not: {1}", who, change);
            }
        }

        // A save that changes one behind the aggregate's back is refused as a policy's refusal is: access.refused.
        await using var services = new TenancyServices(database);
        var linking = await FluentActions.Awaiting(() => services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
            {
                var tenancy = scoped.Tenancy();
                var oli = await tenancy.Set<HostSeat>().SingleAsync(seat => seat.Id == Oli.Seat, Cancellation);
                tenancy.Entry(oli).Property(seat => seat.Identity).CurrentValue = Guid.NewGuid();
                await tenancy.SaveChangesAsync(Cancellation);
            }))
            .Should().ThrowAsync<RefusalException>();
        linking.Which.Code.Should().Be(ToolkitRefusals.Refused);
        linking.Which.Kind.Should().Be(RefusalKind.NotPermitted);
        linking.Which.InnerException.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be(SeatIsFixed);

        (await OlisSeatAsync(database)).Should().Be((Oli.Identity, Harbor.Value), "no refused statement changed it");
    }

    [Fact]
    public async Task System_work_makes_seats_and_links_one_to_another_identity_and_moves_none_to_another_tenant()
    {
        var database = await ForcedAsync();
        await using var services = new TenancyServices(database);

        // Provisioning makes a tenant with its first seat, and an import a seat with the id and the identity it brings:
        // inserts, which the trigger is not about.
        var dock = new TenantId(40);
        var first = Guid.NewGuid();
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision("dock", "Dock Works", TenantShape.Flat, "Dock", first, TenantId: dock, ConfigureFirstSeat: seat => seat.ChangeJobTitle("Dock master")),
                Cancellation));
        }

        var imported = SeatId.CreateSequential();
        var person = Guid.NewGuid();
        await services.BySystemIn(Harbor, scoped => scoped.Seats().AddSeatAsync(person, Cancellation, imported, seat => seat.Rename("Imported")));
        (await services.BySystemIn(dock, scoped => scoped.Tenancy().Set<HostSeat>().CountAsync(seat => seat.Identity == first, Cancellation))).Should().Be(1);
        (await services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<HostSeat>().CountAsync(seat => seat.Id == imported && seat.Identity == person, Cancellation))).Should().Be(1);

        // No use case changes a seat's identity. A one-off of the application's own, as Tenancy's system work in the
        // seat's tenant, links Oli's seat to the identity a new sign-in provider gave him, the way the docs give it.
        var relinked = Guid.NewGuid();
        (await services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<HostSeat>()
            .Where(seat => seat.Id == Oli.Seat)
            .ExecuteUpdateAsync(set => set.SetProperty(seat => seat.Identity, relinked), Cancellation))).Should().Be(1);
        (await OlisSeatAsync(database)).Should().Be((relinked, Harbor.Value));

        // It moves the seat to no other tenant: the policy that keeps system work to its tenant refuses the new row.
        await using (var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            var moved = (await FluentActions.Awaiting(() => system.AttemptAsync("UPDATE tenancy.\"Seats\" SET \"TenantId\" = 2 WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value))
                .Should().ThrowAsync<PostgresException>()).Which;
            (moved.SqlState, moved.Hint).Should().Be((PostgresErrorCodes.InsufficientPrivilege, null), "a policy refused the new row, and no trigger");
        }

        (await OlisSeatAsync(database)).Should().Be((relinked, Harbor.Value));

        // Another module's system work writes none of Tenancy's rows, and the role that owns the tables, which bypasses
        // every policy, is held by the trigger: a migration changes what a seat is no more than a seat does.
        await using (var widgets = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation))
        {
            (await widgets.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Identity\" = gen_random_uuid() WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value)).Should().Be(0);
        }

        await using var owner = new NpgsqlConnection(database.SuperuserConnectionString);
        await owner.OpenAsync(Cancellation);
        await using var changing = new NpgsqlCommand(
            $"SET ROLE {TenancyPostgres.MigrationRole}; UPDATE tenancy.\"Seats\" SET \"Identity\" = gen_random_uuid() WHERE \"Id\" = '{Oli.Seat.Value}'",
            owner);
        (await FluentActions.Awaiting(() => changing.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be(SeatIsFixed);
    }

    [Fact]
    public async Task A_seats_status_still_changes_only_by_a_seat_that_could_give_and_take_away_what_it_holds()
    {
        var database = await ForcedAsync();

        // Hiro may write Seth's row, and suspending Seth would take away the grants Hiro cannot.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            var refused = (await FluentActions.Awaiting(() => hiro.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = 'Suspended' WHERE \"Id\" = $1", Cancellation, Seth.Seat.Value))
                .Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.Hint, refused.ConstraintName).Should().Be((PostgresErrorCodes.InsufficientPrivilege, "ddd:access.refused", StatusIsManaged));
        }

        // Ada suspends and reactivates Oli through the use cases.
        await using var services = new TenancyServices(database);
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().SuspendAsync(Oli.Seat, Cancellation));
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().ReactivateAsync(Oli.Seat, Cancellation));
        (await services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<HostSeat>().Where(seat => seat.Id == Oli.Seat).Select(seat => seat.Status).SingleAsync(Cancellation)))
            .Should().Be(SeatStatus.Active);
    }

    [Fact]
    public async Task Every_use_case_that_writes_a_seat_does_so_for_the_least_its_request_admits_and_the_policy_takes_the_keys_wherever_held_and_keeps_out_the_rest()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);

        // Eve gets a desk that manages seats at North and nothing else, as Hiro's gives roles there and nothing else.
        await HoldAtAsync(database, Eve, North, [TenancyKeys.SeatsManage], Cancellation);
        await TenancyPostgres.ForceAsync(database, Cancellation);
        await using var services = new TenancyServices(database);

        // Every save of a seat writes its version: a grant and a placement as much as a status. So the policy on the
        // seats asks the grants key as well: Hiro gives Oli a role at North Pier and takes it away again, which changes
        // Oli's row each time.
        await WritesOlisRowAsync(Hiro, seats => seats.GrantAsync(Oli.Seat, NorthPier, HarborRoles.Watcher, until: null, reason: null, Cancellation));
        await WritesOlisRowAsync(Hiro, seats => seats.RevokeAsync(Oli.Seat, NorthPier, HarborRoles.Watcher, Cancellation));

        // The policy asks the grants key held anywhere, not at the seat's units, on purpose: it is all that lets the
        // application's own commands write the row, and holding it to the units would decide for the application which
        // of its fields a grants manager writes. So Hiro, who gives roles at North alone, writes the version of Sue, at
        // South, which no use case of his would; it changes nothing she holds.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await hiro.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Version\" = \"Version\" + 1 WHERE \"Id\" = $1", Cancellation, Sue.Seat.Value)).Should().Be(1);
        }

        // Eve places Oli at North, makes it his primary placement and North Pier again, and withdraws him, with the
        // seats key alone.
        await WritesOlisRowAsync(Eve, seats => seats.PlaceAsync(Oli.Seat, North, primary: false, Cancellation));
        await WritesOlisRowAsync(Eve, seats => seats.MakePrimaryAsync(Oli.Seat, North, Cancellation));
        await WritesOlisRowAsync(Eve, seats => seats.MakePrimaryAsync(Oli.Seat, NorthPier, Cancellation));
        await WritesOlisRowAsync(Eve, seats => seats.WithdrawAsync(Oli.Seat, North, Cancellation));

        // Hiro takes away his own Grants desk: the save takes his key with it, and the row is his own all the same.
        await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().RevokeAsync(Hiro.Seat, North, GrantsDesk, Cancellation));

        // A seat that manages neither writes no other seat's row, the version included: the row is not its to find.
        // Hiro, his desk taken away, is one now.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await hiro.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Version\" = \"Version\" + 1 WHERE \"Id\" = $1", Cancellation, Sue.Seat.Value)).Should().Be(0);
        }

        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Version\" = \"Version\" + 1 WHERE \"Id\" = $1", Cancellation, Sue.Seat.Value)).Should().Be(0);
            (await oli.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Version\" = \"Version\" + 1 WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value)).Should().Be(1, "his own row is his");
        }

        // Runs a use case as the seat of the caller, and says that it wrote Oli's row: his version went up.
        async Task WritesOlisRowAsync(Person caller, Func<HostTenancy.SeatCommands, Task> act)
        {
            var before = await OlisVersionAsync();
            await services.BySeat(caller.Identity, Harbor, caller.Seat, scoped => act(scoped.Seats()));
            (await OlisVersionAsync()).Should().BeGreaterThan(before, "the save of {0}'s use case wrote Oli's row", caller.Name);
        }

        Task<long> OlisVersionAsync()
            => services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<HostSeat>().Where(seat => seat.Id == Oli.Seat).Select(seat => seat.Version).SingleAsync(Cancellation));
    }

    [Fact]
    public async Task A_field_of_the_applications_own_is_as_writable_as_the_row_until_a_rule_of_its_own_holds_it()
    {
        var database = await ForcedAsync(withJobTitles: true);
        const string Retitling = "UPDATE tenancy.\"Seats\" SET \"JobTitle\" = 'Foreman' WHERE \"Id\" = $1";
        const string Renaming = "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Sue, at South' WHERE \"Id\" = $1";

        // The name has no rule of the application's: whoever may write the row writes it, Tenancy decides nothing.
        foreach (var (caller, seat) in new[] { (Hiro, Sue), (Seth, Sue), (Oli, Oli) })
        {
            await using var asCaller = await AsCaller.PersonAsync(database, caller.Identity, Harbor, Cancellation);
            (await asCaller.ExecuteAsync(Renaming, Cancellation, seat.Seat.Value)).Should().Be(1, "{0} may write {1}'s row", caller.Name, seat.Name);
        }

        // The job title has one: it changes with the seats key for the whole tenant, which neither Hiro, Seth nor Oli
        // holds, the seat's own row included.
        foreach (var (caller, seat) in new[] { (Hiro, Sue), (Seth, Sue), (Oli, Oli) })
        {
            await using var asCaller = await AsCaller.PersonAsync(database, caller.Identity, Harbor, Cancellation);
            var refused = (await FluentActions.Awaiting(() => asCaller.ExecuteAsync(Retitling, Cancellation, seat.Seat.Value)).Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.Hint, refused.ConstraintName).Should().Be((PostgresErrorCodes.InsufficientPrivilege, "ddd:access.refused", JobTitleRule), "{0} on {1}'s seat", caller.Name, seat.Name);
        }

        // Ada holds it, and system work is the application's own work, which a column rule does not hold.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync(Retitling, Cancellation, Sue.Seat.Value)).Should().Be(1);
        }

        await using (var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await system.ExecuteAsync(Retitling, Cancellation, Oli.Seat.Value)).Should().Be(1);
        }

        // Through the application's own command, a save the rule refuses is access.refused, as a policy's is.
        await using var services = new TenancyServices(database);
        var retitling = await FluentActions.Awaiting(() => services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, async scoped =>
            {
                var store = scoped.GetRequiredService<HostTenancy.IStore>();
                var sue = (await store.FindSeatAsync(Sue.Seat, Cancellation))!;
                sue.ChangeJobTitle("Foreman");
                await store.SaveAsync(Cancellation);
            }))
            .Should().ThrowAsync<RefusalException>();
        retitling.Which.Code.Should().Be(ToolkitRefusals.Refused);
    }

    /// <summary>A copy of the secured template, forced, with the application's column rule on the job title where asked.</summary>
    private async Task<TestDatabase> ForcedAsync(bool withJobTitles = false)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ForceAsync(database, Cancellation, withJobTitles ? [.. WidgetRules.All, JobTitles] : null);
        return database;
    }

    /// <summary>Oli's seat in Harbor as it is stored: its identity and its tenant, read by Tenancy's system work in Harbor.</summary>
    private static async Task<(Guid Identity, long Tenant)> OlisSeatAsync(TestDatabase database)
    {
        await using var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation);
        var identity = await system.ScalarAsync<Guid>("SELECT \"Identity\" FROM tenancy.\"Seats\" WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value);
        var tenant = await system.ScalarAsync<long>("SELECT \"TenantId\" FROM tenancy.\"Seats\" WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value);
        return (identity, tenant);
    }
}

/// <summary>
/// The application's own rule about a field of its own on its seat class: a job title changes only for a seat that
/// manages seats for the whole tenant. Tenancy decides nothing about the field, so this rule is all that holds it.
/// </summary>
[RowAccess<HostSeat>(RowOperations.Change, To = [RowAccessRoles.User], Columns = [nameof(HostSeat.JobTitle)])]
public static partial class JobTitlesChangeWithTheSeatsKeyForTheWholeTenant
{
    /// <summary>Whether the calling seat manages seats for the whole tenant.</summary>
    public static bool Allows(HostSeat seat, Caller caller) => TenancyRowAccess.HoldsTenantWide(TenancyKeys.SeatsManage);
}
