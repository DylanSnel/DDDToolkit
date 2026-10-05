using DDDToolkit.Supporting.Tenancy.Catalogue;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The triggers keep what no policy can see, because it is about other rows than the one written: a tenant keeps an
/// administrator, every right comes from a grant, the closure is the tree, and a seat keeps its identity and tenant.
/// Each statement here runs as the caller, past the use cases, as the application's own SQL that forgot a rule would;
/// the policies let it through, and the trigger refuses it when the transaction commits. The use cases never trip them.
/// The rights themselves no caller writes: the trigger that keeps them writes them with the grant, the seat or the
/// role, and what it takes away is what the administrator's trigger then checks.
/// </summary>
public abstract class TenancyTriggerTests(TenancyPostgres postgres, TenancyNaming names)
{
    private const string AdministratorRemains = "tenancy_administrator_remains";

    private const string RightsBackedByGrants = "tenancy_rights_backed_by_grants";

    private const string PathsFollowTheTree = "tenancy_paths_follow_the_tree";

    private const string SeatStatusIsManaged = "tenancy_seat_status_is_managed";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Removing_the_last_administrators_grant_by_a_query_as_the_caller_fails_at_commit()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation);

        // Her own grant, which a seat may take away: the rights it gave go with it, in the same statement.
        (await ada.ExecuteAsync("DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Ada.Seat.Value, HarborRoles.Administrator.Value)).Should().Be(1);
        (await ada.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Ada.Seat.Value)).Should().Be(0);

        (await FailsAtCommitAsync(ada)).Should().Be(AdministratorRemains);
        await HarborKeepsAdaAsync(database);
    }

    [Fact]
    public async Task Deleting_the_administrators_rights_by_a_query_as_the_caller_is_refused_by_the_policies()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // No policy lets a caller remove a right, her own included: the statement finds no row it may touch, at once,
        // and nothing is left for the commit to check.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync("DELETE FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.roles.manage'", Cancellation, Ada.Seat.Value)).Should().Be(0);
            (await ada.ExecuteAsync("UPDATE tenancy.\"SeatRights\" SET \"EndsAt\" = now() WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.roles.manage'", Cancellation, Ada.Seat.Value)).Should().Be(0);
            await ada.CommitAsync(Cancellation);
        }

        await HarborKeepsAdaAsync(database);

        // The owner is past the policies, and not past the trigger.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ExecuteAsync("DELETE FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.roles.manage'", Cancellation, Ada.Seat.Value)).Should().Be(1);
        (await FailsAtCommitAsync(owner)).Should().Be(AdministratorRemains);
    }

    [Fact]
    public async Task Suspending_the_last_administrator_by_a_query_as_the_caller_fails_at_commit()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Her rights go with her seat's status, in the same statement, and the last administrator with them.
        await using var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation);
        (await ada.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = 'Suspended' WHERE \"Id\" = $1", Cancellation, Ada.Seat.Value)).Should().Be(1);

        (await FailsAtCommitAsync(ada)).Should().Be(AdministratorRemains);
        await HarborKeepsAdaAsync(database);
    }

    [Fact]
    public async Task A_query_changes_a_seats_status_only_as_a_seat_that_could_give_and_take_away_its_grants()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        const string reactivating = "UPDATE tenancy.\"Seats\" SET \"Status\" = 'Active' WHERE \"Id\" = $1";
        const string suspending = "UPDATE tenancy.\"Seats\" SET \"Status\" = 'Suspended' WHERE \"Id\" = $1";
        const string rights = "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1";

        // Hiro gives roles at North and manages no seats. The policy lets a grants manager change a seat's row, and
        // the rights follow a seat's status: by a query that forgot a condition he would give Sue, suspended at South,
        // every grant back, and take Seth's and Ada's away.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await StatusRefusedAsync(hiro, reactivating, Sue)).Should().Be(SeatStatusIsManaged);
            (await StatusRefusedAsync(hiro, suspending, Seth)).Should().Be(SeatStatusIsManaged);
            (await StatusRefusedAsync(hiro, "UPDATE tenancy.\"Seats\" SET \"Status\" = 'Deactivated' WHERE \"Id\" = $1", Ada)).Should().Be(SeatStatusIsManaged);

            // What a seat's row says besides, and the status it has already, he writes.
            (await hiro.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Sue, at South', \"Status\" = \"Status\" WHERE \"Id\" = $1", Cancellation, Sue.Seat.Value)).Should().Be(1);
        }

        // Seth manages seats at North and below, and not for the whole tenant, which a change of status takes.
        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await StatusRefusedAsync(seth, reactivating, Sue)).Should().Be(SeatStatusIsManaged);
            (await StatusRefusedAsync(seth, suspending, Oli)).Should().Be(SeatStatusIsManaged);
        }

        // Oli gets a desk that manages seats for the whole tenant, and nothing else.
        await HoldAtAsync(database, Oli, HarborRoot, [TenancyKeys.SeatsManage], Cancellation);
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            // Sue's role manages no access, so her seat is his to reactivate, and her rights come back with it.
            (await oli.ScalarAsync<long>(rights, Cancellation, Sue.Seat.Value)).Should().Be(0);
            (await oli.ExecuteAsync(reactivating, Cancellation, Sue.Seat.Value)).Should().Be(1);
            await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
            {
                (await owner.ScalarAsync<long>(rights, Cancellation, Sue.Seat.Value)).Should().Be(0, "not before he commits");
            }

            // Hiro's role gives tenancy.grants.manage at North, which Oli does not hold there: taking it away by
            // suspending him is no more his than revoking the grant would be. Ada's he holds even less.
            (await StatusRefusedAsync(oli, suspending, Hiro)).Should().Be(SeatStatusIsManaged);
            (await StatusRefusedAsync(oli, suspending, Ada)).Should().Be(SeatStatusIsManaged);

            // His own grants are his own hold: he suspends himself.
            (await oli.ExecuteAsync(suspending, Cancellation, Oli.Seat.Value)).Should().Be(1);
            await oli.CommitAsync(Cancellation);
        }

        // Ada holds every key at the root, for good.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync(suspending, Cancellation, Hiro.Seat.Value)).Should().Be(1);
            (await ada.ExecuteAsync(reactivating, Cancellation, Hiro.Seat.Value)).Should().Be(1);
            (await ada.ExecuteAsync(reactivating, Cancellation, Oli.Seat.Value)).Should().Be(1);
            await ada.CommitAsync(Cancellation);
        }

        // System work is no seat, and neither is the tables' owner: the policies, and the administrator a tenant keeps,
        // are what hold those.
        await using (var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await system.ExecuteAsync(suspending, Cancellation, Seth.Seat.Value)).Should().Be(1);
            (await system.ExecuteAsync(reactivating, Cancellation, Seth.Seat.Value)).Should().Be(1);
        }

        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ExecuteAsync(suspending, Cancellation, Seth.Seat.Value)).Should().Be(1);
        (await after.ExecuteAsync(reactivating, Cancellation, Seth.Seat.Value)).Should().Be(1);
        (await after.ScalarAsync<long>(rights, Cancellation, Sue.Seat.Value)).Should().BePositive("Oli gave her seat back what it holds");
        (await after.ListAsync<string>("SELECT \"Status\" FROM tenancy.\"Seats\" WHERE \"Id\" = ANY ($1) ORDER BY \"Id\"", Cancellation, (object)new[] { Ada.Seat.Value, Hiro.Seat.Value, Seth.Seat.Value }))
            .Should().AllBe(database.Names.Stored(SeatStatus.Active), "no refused statement changed a seat");

        static async Task<string?> StatusRefusedAsync(AsCaller caller, string change, Person seat)
        {
            var refused = (await FluentActions.Awaiting(() => caller.AttemptAsync(change, Cancellation, seat.Seat.Value)).Should().ThrowAsync<PostgresException>(change)).Which;
            // Refused as a policy refuses, since it holds who may: 42501 with the toolkit's hint, which a use case's save
            // answers with access.refused.
            (refused.SqlState, refused.Hint).Should().Be((PostgresErrorCodes.InsufficientPrivilege, "ddd:access.refused"));
            return refused.ConstraintName;
        }
    }

    [Fact]
    public async Task Taking_the_administrator_key_from_the_last_admin_role_by_a_query_as_the_caller_fails_at_commit()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        const string takingTheKey = "UPDATE tenancy.\"Roles\" SET \"Keys\" = pg_catalog.array_remove(\"Keys\", 'tenancy.roles.manage') WHERE \"Id\" = $1";

        // The role loses the key, and its holders the rights for it, in the same statement.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync(takingTheKey, Cancellation, HarborRoles.Administrator.Value)).Should().Be(1);
            (await ada.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.roles.manage'", Cancellation, Ada.Seat.Value)).Should().Be(0);

            (await FailsAtCommitAsync(ada)).Should().Be(AdministratorRemains);
        }

        await HarborKeepsAdaAsync(database);
    }

    [Fact]
    public async Task A_tenant_without_an_administrator_is_not_frozen()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await QuayLosesQuinAsync(database);

        // Quay is active again, with no administrator: nothing it does is refused for lacking one.
        await using (var quay = await AsCaller.SystemInAsync(database, Quay, TenancyWork.SystemScope, Cancellation))
        {
            (await quay.ExecuteAsync("UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Active', \"StatusReason\" = NULL WHERE \"Id\" = 3", Cancellation)).Should().Be(1);
            await quay.CommitAsync(Cancellation);
        }

        await using (var quay = await AsCaller.SystemInAsync(database, Quay, TenancyWork.SystemScope, Cancellation))
        {
            (await quay.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Quin, who left' WHERE \"Id\" = $1", Cancellation, Quin.Seat.Value)).Should().Be(1);
            (await quay.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Status\" = 'Archived' WHERE \"Id\" = $1", Cancellation, QuayRoles.Watcher.Value)).Should().Be(1);
            (await quay.ExecuteAsync("DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1", Cancellation, Quin.Seat.Value)).Should().Be(1);
            await quay.CommitAsync(Cancellation);
        }

        // Nor when a seat gets its rights back: Quin's seat counts again, and has no grant left to hold anything by.
        await using var again = await AsCaller.SystemInAsync(database, Quay, TenancyWork.SystemScope, Cancellation);
        (await again.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = 'Active' WHERE \"Id\" = $1", Cancellation, Quin.Seat.Value)).Should().Be(1);
        await again.CommitAsync(Cancellation);
    }

    [Fact]
    public async Task A_closed_tenant_may_lose_its_last_administrator()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Suspended, Quay keeps Quin.
        await using (var quay = await AsCaller.SystemInAsync(database, Quay, TenancyWork.SystemScope, Cancellation))
        {
            (await quay.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = 'Deactivated' WHERE \"Id\" = $1", Cancellation, Quin.Seat.Value)).Should().Be(1);
            (await FailsAtCommitAsync(quay)).Should().Be(AdministratorRemains);
        }

        // Closed, it needs none.
        await QuayLosesQuinAsync(database);
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"TenantId\" = 3", Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task A_rights_row_no_grant_backs_is_refused_at_commit()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Only the owner writes a right past the policies, in a migration or an import say. Oli has no grant at South.
        await using (var ada = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await ada.ExecuteAsync(
                    "INSERT INTO tenancy.\"SeatRights\" (\"SeatId\", \"UnitId\", \"RoleId\", \"Key\", \"TenantId\", \"StartsAt\") VALUES ($1, $2, $3, 'widget.read', 1, now())",
                    Cancellation,
                    Oli.Seat.Value,
                    South.Value,
                    HarborRoles.Watcher.Value))
                .Should().Be(1);

            (await FailsAtCommitAsync(ada)).Should().Be(RightsBackedByGrants);
        }

        // Nor may a right outlast its grant, or hold a key its role does not.
        await using (var ada = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await ada.ExecuteAsync("UPDATE tenancy.\"SeatRights\" SET \"EndsAt\" = NULL WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Eve.Seat.Value, HarborRoles.Watcher.Value))
                .Should().Be(1);
            (await FailsAtCommitAsync(ada)).Should().Be(RightsBackedByGrants);
        }

        await using (var ada = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await ada.ExecuteAsync(
                    "INSERT INTO tenancy.\"SeatRights\" (\"SeatId\", \"UnitId\", \"RoleId\", \"Key\", \"TenantId\", \"StartsAt\") SELECT \"SeatId\", \"UnitId\", \"RoleId\", 'tenancy.roles.manage', 1, \"StartsAt\" FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2",
                    Cancellation,
                    Oli.Seat.Value,
                    HarborRoles.Operator.Value))
                .Should().Be(1);
            (await FailsAtCommitAsync(ada)).Should().Be(RightsBackedByGrants);
        }
    }

    [Fact]
    public async Task Deleting_a_grant_deletes_its_rights_in_the_same_statement()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        const string hisRights = "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1";

        // Oli gives up his own Operator role at North Pier, by a query: before the next statement of his transaction
        // the rights it gave are gone, and the function that asks them answers so.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ScalarAsync<long>(hisRights, Cancellation, Oli.Seat.Value)).Should().Be(3);
            (await oli.ExecuteAsync("DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Oli.Seat.Value, HarborRoles.Operator.Value)).Should().Be(1);
            (await oli.ScalarAsync<long>(hisRights, Cancellation, Oli.Seat.Value)).Should().Be(0);
            (await oli.ScalarAsync<bool>("SELECT tenancy.holds_key('widget.change')", Cancellation)).Should().BeFalse();
        }

        // Rolled back, he holds them still. Hiro, who gives roles there, takes the role away and commits: nothing is left
        // behind for the trigger that backs every right with a grant to refuse.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await hiro.ExecuteAsync("DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Oli.Seat.Value, HarborRoles.Operator.Value)).Should().Be(1);
            await hiro.CommitAsync(Cancellation);
        }

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>(hisRights, Cancellation, Oli.Seat.Value)).Should().Be(0);
    }

    [Fact]
    public async Task A_closure_row_that_does_not_follow_the_tree_is_refused_at_commit()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Seth manages units at North, which lets him write the closure: a row too few, a distance wrong, a unit that is
        // not there.
        string[] wrong =
        [
            "DELETE FROM tenancy.\"OrganizationUnitPaths\" WHERE \"AncestorId\" = $1 AND \"DescendantId\" = $2",
            "UPDATE tenancy.\"OrganizationUnitPaths\" SET \"Distance\" = 2 WHERE \"AncestorId\" = $1 AND \"DescendantId\" = $2",
            "INSERT INTO tenancy.\"OrganizationUnitPaths\" (\"AncestorId\", \"DescendantId\", \"TenantId\", \"Distance\") VALUES ($1, gen_random_uuid(), 1, 1)",
        ];

        foreach (var statement in wrong)
        {
            await using var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);
            (await seth.ExecuteAsync(statement, Cancellation, statement.StartsWith("INSERT", StringComparison.Ordinal) ? [North.Value] : [North.Value, NorthPier.Value])).Should().Be(1);
            (await FailsAtCommitAsync(seth)).Should().Be(PathsFollowTheTree, statement);
        }
    }

    [Fact]
    public async Task Moving_a_unit_through_the_use_case_keeps_the_closure_check_green()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        var berth = OrganizationUnitId.CreateSequential();

        // A unit below North Pier, so the move takes a subtree along.
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Organization().AddUnitAsync(NorthPier, "Berth", Cancellation, berth));
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Organization().MoveUnitAsync(NorthPier, South, Cancellation));
        await services.BySystemIn(Harbor, scoped => scoped.Organization().MoveUnitAsync(NorthPier, North, Cancellation));
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Organization().MoveUnitAsync(NorthPier, HarborRoot, Cancellation));

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>(
                """
                SELECT a."Name" || ' ' || p."Distance" FROM tenancy."OrganizationUnitPaths" p
                JOIN tenancy."OrganizationUnits" a ON a."Id" = p."AncestorId"
                WHERE p."DescendantId" = $1 ORDER BY p."Distance"
                """,
                Cancellation,
                berth.Value))
            .Should().Equal("Berth 0", "North Pier 1", "Harbor 2");
    }

    [Fact]
    public async Task A_seats_identity_or_tenant_cannot_change_by_a_query()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        string[] changes =
        [
            "UPDATE tenancy.\"Seats\" SET \"Identity\" = gen_random_uuid() WHERE \"Id\" = $1",
            "UPDATE tenancy.\"Seats\" SET \"TenantId\" = 2 WHERE \"Id\" = $1",
        ];

        foreach (var change in changes)
        {
            // Ada may change any seat of Harbor; the owner is past every policy. Neither changes these.
            await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
            {
                (await IdentityRefusedAsync(ada, change)).Should().Be("tenancy_seat_identity_is_fixed");
            }

            await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
            {
                (await IdentityRefusedAsync(owner, change)).Should().Be("tenancy_seat_identity_is_fixed");
            }
        }

        // Anything else about the seat changes, and the same values again are no change.
        await using var same = await AsCaller.OwnerAsync(database, Cancellation);
        (await same.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Identity\" = \"Identity\", \"TenantId\" = \"TenantId\", \"DisplayName\" = 'Oliver' WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value))
            .Should().Be(1);
        await same.CommitAsync(Cancellation);

        async Task<string?> IdentityRefusedAsync(AsCaller caller, string change)
            => (await FluentActions.Awaiting(() => caller.ExecuteAsync(change, Cancellation, Oli.Seat.Value)).Should().ThrowAsync<PostgresException>()).Which.ConstraintName;
    }

    [Fact]
    public async Task A_placements_seat_unit_or_tenant_and_a_grants_seat_unit_role_or_giver_cannot_change_by_a_query()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        (string Change, string Trigger)[] changes =
        [
            ("UPDATE tenancy.\"SeatPlacements\" SET \"SeatId\" = $2 WHERE \"SeatId\" = $1 AND \"UnitId\" = $4 AND $3 = $3", "tenancy_placement_is_fixed"),
            ("UPDATE tenancy.\"SeatPlacements\" SET \"UnitId\" = $3 WHERE \"SeatId\" = $1 AND \"UnitId\" = $4 AND $2 = $2", "tenancy_placement_is_fixed"),
            ("UPDATE tenancy.\"SeatPlacements\" SET \"TenantId\" = 2 WHERE \"SeatId\" = $1 AND \"UnitId\" = $4 AND $2 = $2 AND $3 = $3", "tenancy_placement_is_fixed"),
            ("UPDATE tenancy.\"SeatRoleGrants\" SET \"SeatId\" = $2 WHERE \"SeatId\" = $1 AND \"UnitId\" = $4 AND $3 = $3", "tenancy_grant_is_fixed"),
            ("UPDATE tenancy.\"SeatRoleGrants\" SET \"UnitId\" = $3 WHERE \"SeatId\" = $1 AND \"UnitId\" = $4 AND $2 = $2", "tenancy_grant_is_fixed"),
            ("UPDATE tenancy.\"SeatRoleGrants\" SET \"RoleId\" = '" + HarborRoles.Watcher.Value + "' WHERE \"SeatId\" = $1 AND \"UnitId\" = $4 AND $2 = $2 AND $3 = $3", "tenancy_grant_is_fixed"),
            ("UPDATE tenancy.\"SeatRoleGrants\" SET \"GrantedBy\" = $2 WHERE \"SeatId\" = $1 AND \"UnitId\" = $4 AND $3 = $3", "tenancy_grant_is_fixed"),
        ];

        // The owner is past every policy, and still moves neither Oli's placement at North Pier nor his grant there.
        foreach (var (change, trigger) in changes)
        {
            await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
            (await FluentActions.Awaiting(() => owner.ExecuteAsync(change, Cancellation, Oli.Seat.Value, Eve.Seat.Value, South.Value, NorthPier.Value))
                    .Should().ThrowAsync<PostgresException>(change))
                .Which.ConstraintName.Should().Be(trigger, change);
        }

        // What a placement or a grant says besides changes, and the same values again are no change.
        await using var same = await AsCaller.OwnerAsync(database, Cancellation);
        (await same.ExecuteAsync("UPDATE tenancy.\"SeatPlacements\" SET \"SeatId\" = \"SeatId\", \"UnitId\" = \"UnitId\", \"TenantId\" = \"TenantId\", \"PlacedBy\" = NULL WHERE \"SeatId\" = $1", Cancellation, Oli.Seat.Value))
            .Should().Be(1);
        (await same.ExecuteAsync("UPDATE tenancy.\"SeatRoleGrants\" SET \"GrantedBy\" = \"GrantedBy\", \"RoleId\" = \"RoleId\", \"Reason\" = 'Standing in' WHERE \"SeatId\" = $1", Cancellation, Oli.Seat.Value))
            .Should().Be(1);
        await same.CommitAsync(Cancellation);
    }

    [Fact]
    public async Task Two_transactions_that_each_take_away_another_administrator_cannot_both_commit()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldAtAsync(database, Hiro, HarborRoot, keys: null, Cancellation);
        const string takingAway = "DELETE FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.roles.manage'";

        // Harbor has two administrators, Ada and Hiro. Each transaction takes one away, and checks before the other ends.
        await using var first = await AsCaller.OwnerAsync(database, Cancellation);
        await using var second = await AsCaller.OwnerAsync(database, Cancellation);
        (await first.ExecuteAsync(takingAway, Cancellation, Ada.Seat.Value)).Should().Be(1);
        (await second.ExecuteAsync(takingAway, Cancellation, Hiro.Seat.Value)).Should().Be(1);
        await first.ExecuteAsync("SET CONSTRAINTS ALL IMMEDIATE", Cancellation);

        // The second waits for the first to end, and then sees that it took Ada away.
        var checking = second.ExecuteAsync("SET CONSTRAINTS ALL IMMEDIATE", Cancellation);
        await using (var watcher = await AsCaller.OwnerAsync(database, Cancellation))
        {
            var waited = 0;
            while (await watcher.ScalarAsync<long>("SELECT count(*) FROM pg_catalog.pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())", Cancellation) == 0)
            {
                checking.IsCompleted.Should().BeFalse("the second check waits for the first transaction");
                (++waited).Should().BeLessThan(200);
                await Task.Delay(50, Cancellation);
            }
        }

        await first.CommitAsync(Cancellation);
        (await FluentActions.Awaiting(() => checking).Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be(AdministratorRemains);

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"Key\" = 'tenancy.roles.manage' AND \"TenantId\" = 1 AND \"UnitId\" = $1", Cancellation, HarborRoot.Value))
            .Should().Be(1, "Hiro is left");
    }

    /// <summary>Commits, expecting a deferred trigger to refuse it, and says which.</summary>
    private static async Task<string?> FailsAtCommitAsync(AsCaller caller)
    {
        var failure = await FluentActions.Awaiting(() => caller.CommitAsync(Cancellation)).Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        return failure.Which.ConstraintName;
    }

    /// <summary>Harbor's administrator is as seeded: the refused transactions changed nothing.</summary>
    private static async Task HarborKeepsAdaAsync(TestDatabase database)
    {
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>(
                "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.roles.manage' AND \"UnitId\" = $2",
                Cancellation,
                Ada.Seat.Value,
                HarborRoot.Value))
            .Should().Be(1);
        (await owner.ScalarAsync<string>("SELECT \"Status\" FROM tenancy.\"Seats\" WHERE \"Id\" = $1", Cancellation, Ada.Seat.Value)).Should().Be(database.Names.Stored(SeatStatus.Active));
        (await owner.ScalarAsync<bool>("SELECT 'tenancy.roles.manage' = ANY (\"Keys\") FROM tenancy.\"Roles\" WHERE \"Id\" = $1", Cancellation, HarborRoles.Administrator.Value)).Should().BeTrue();
    }

    /// <summary>Quay is closed, and loses Quin, its one administrator: system work of Tenancy's, which commits.</summary>
    private static async Task QuayLosesQuinAsync(TestDatabase database)
    {
        await using var quay = await AsCaller.SystemInAsync(database, Quay, TenancyWork.SystemScope, Cancellation);
        (await quay.ExecuteAsync("UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Closed', \"StatusReason\" = 'Wound up' WHERE \"Id\" = 3", Cancellation)).Should().Be(1);
        (await quay.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = 'Deactivated' WHERE \"Id\" = $1", Cancellation, Quin.Seat.Value)).Should().Be(1);
        (await quay.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Quin.Seat.Value)).Should().Be(0, "his rights went with his seat");
        await quay.CommitAsync(Cancellation);
    }
}

/// <summary>The triggers, under the names Entity Framework gives the tables and columns.</summary>
public sealed class TenancyTriggerTestsOnDefaultNames(TenancyPostgres postgres) : TenancyTriggerTests(postgres, TenancyNaming.Default);

/// <summary>The triggers, under snake_case names with enums stored as snake_case text.</summary>
public sealed class TenancyTriggerTestsOnSnakeCase(TenancyPostgres postgres) : TenancyTriggerTests(postgres, TenancyNaming.SnakeCase);
