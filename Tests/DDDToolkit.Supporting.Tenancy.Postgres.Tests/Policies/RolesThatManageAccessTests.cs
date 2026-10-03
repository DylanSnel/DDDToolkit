using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The policies on the grants table know which roles manage access, from the catalogue the application runs with,
/// and hold a query of the application's own to the same rule the use cases keep: a role that manages access is
/// given and taken away only by a seat that holds each of its keys that do at the grant's unit, and never given by
/// a seat to itself; any other role goes by the grants key alone. Each query here runs as the caller, past the use
/// cases. How long the caller holds the keys is the use cases' alone.
/// </summary>
/// <remarks>
/// Harbor's Grants desk role holds <c>tenancy.grants.manage</c> alone; Hiro has it at North. Seth is a Supervisor at
/// North, which manages units, seats and grants there. Oli is placed at North Pier, below North, and so is Seth.
/// </remarks>
public abstract class RolesThatManageAccessTests(TenancyPostgres postgres, TenancyNaming names)
{
    private const string Give =
        """
        INSERT INTO tenancy."SeatRoleGrants" ("SeatId", "UnitId", "RoleId", "StartsAt", "EndsAt", "GrantedBy", "Reason")
        VALUES ($1, $2, $3, now(), NULL, $4, NULL)
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_grants_manager_without_its_keys_cannot_give_a_role_that_manages_access_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);

        // Hiro gives roles at North Pier, but holds none of the Supervisor's keys that manage access but the grants key.
        var giving = () => hiro.ExecuteAsync(Give, Cancellation, Oli.Seat.Value, NorthPier.Value, HarborRoles.Supervisor.Value, Hiro.Seat.Value);
        (await giving.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_holder_of_its_keys_gives_a_role_that_manages_access_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await seth.ExecuteAsync(Give, Cancellation, Oli.Seat.Value, NorthPier.Value, HarborRoles.Supervisor.Value, Seth.Seat.Value)).Should().Be(1);
            await seth.CommitAsync(Cancellation);
        }

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>(
                "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2",
                Cancellation,
                Oli.Seat.Value,
                HarborRoles.Supervisor.Value))
            .Should().Be(1);
    }

    [Fact]
    public async Task No_seat_gives_itself_a_role_that_manages_access_by_a_query_as_the_caller_even_holding_its_keys()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);

        // Seth holds every key of the Supervisor at North Pier, where Seth is placed too.
        var appointing = () => seth.ExecuteAsync(Give, Cancellation, Seth.Seat.Value, NorthPier.Value, HarborRoles.Supervisor.Value, Seth.Seat.Value);
        (await appointing.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_role_that_manages_no_access_goes_by_the_grants_key_alone_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);

        // An Operator works with widgets, whose keys Hiro does not hold, to someone else and to Hiro.
        (await hiro.ExecuteAsync(Give, Cancellation, Oli.Seat.Value, NorthPier.Value, HarborRoles.Watcher.Value, Hiro.Seat.Value)).Should().Be(1);
        (await hiro.ExecuteAsync(Give, Cancellation, Hiro.Seat.Value, North.Value, HarborRoles.Operator.Value, Hiro.Seat.Value)).Should().Be(1);
        await hiro.CommitAsync(Cancellation);
    }

    [Fact]
    public async Task A_key_the_application_marks_makes_its_roles_contained_in_the_policies()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Marked by the application, widget.create manages access, and the Operator, who holds it, with it.
        var marked = TenancyCatalogue.Build(HostCatalogue.Application with { AccessManagingKeys = [HostCatalogue.WidgetCreate] }, []);
        foreach (var script in TenancyPostgres.AccessScripts(marked, names: names))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            var giving = () => hiro.ExecuteAsync(Give, Cancellation, Oli.Seat.Value, NorthPier.Value, HarborRoles.Operator.Value, Hiro.Seat.Value);
            (await giving.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Seth holds widget.create at North, as a Supervisor, and gives it there.
        await using var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);
        (await seth.ExecuteAsync(Give, Cancellation, Hiro.Seat.Value, North.Value, HarborRoles.Operator.Value, Seth.Seat.Value)).Should().Be(1);
    }

    [Fact]
    public async Task Taking_away_a_role_that_manages_access_needs_its_keys_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Hiro sees Seth's Supervisor grant at North, and cannot take it away.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await hiro.ExecuteAsync(
                    "DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"UnitId\" = $2 AND \"RoleId\" = $3",
                    Cancellation,
                    Seth.Seat.Value,
                    North.Value,
                    HarborRoles.Supervisor.Value))
                .Should().Be(0, "a policy that does not let a row be deleted leaves it where it is");

            // An Operator's grant he takes away, and the rights it gave go with it.
            (await hiro.ExecuteAsync(
                    "DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"UnitId\" = $2 AND \"RoleId\" = $3",
                    Cancellation,
                    Oli.Seat.Value,
                    NorthPier.Value,
                    HarborRoles.Operator.Value))
                .Should().Be(1);
            await hiro.CommitAsync(Cancellation);
        }

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Oli.Seat.Value)).Should().Be(0);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Seth.Seat.Value)).Should().BePositive("Seth's are where they were");
    }

    [Fact]
    public async Task Withdrawing_a_placement_that_holds_a_role_that_manages_access_needs_its_keys_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldAtAsync(database, Eve, North, [TenancyKeys.SeatsManage], Cancellation);
        const string placement = "DELETE FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = $1 AND \"UnitId\" = $2";

        // Eve manages seats at North, and holds none of the Grants desk's keys: Hiro's placement there, which the
        // database would take his grant away with, and his rights with the grant, is not hers to remove.
        await using (var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation))
        {
            (await eve.ExecuteAsync(placement, Cancellation, Hiro.Seat.Value, North.Value)).Should().Be(0);
            await eve.CommitAsync(Cancellation);
        }

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<long>(
                    "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2",
                    Cancellation,
                    Hiro.Seat.Value,
                    GrantsDesk.Value))
                .Should().Be(1, "the grant is where it was");
            (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"UnitId\" = $2", Cancellation, Hiro.Seat.Value, North.Value))
                .Should().Be(1, "and so is the right it gives");
        }

        // Seth holds the grants key at North, and so withdraws Hiro there, his grant first, as the use case does.
        await using var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation);
        (await seth.ExecuteAsync("DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"UnitId\" = $2", Cancellation, Hiro.Seat.Value, North.Value)).Should().Be(1);
        (await seth.ExecuteAsync(placement, Cancellation, Hiro.Seat.Value, North.Value)).Should().Be(1);
        await seth.CommitAsync(Cancellation);
    }

    [Fact]
    public async Task A_grant_changed_into_a_role_that_manages_access_is_refused_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);

        // Otherwise a grants manager would turn a role anyone may give into one only its holders may. A grant keeps its
        // role, whoever changes it, and the policy would not let this caller leave such a grant behind either.
        var changing = () => hiro.ExecuteAsync(
            "UPDATE tenancy.\"SeatRoleGrants\" SET \"RoleId\" = $1 WHERE \"SeatId\" = $2 AND \"UnitId\" = $3 AND \"RoleId\" = $4",
            Cancellation,
            HarborRoles.Supervisor.Value,
            Oli.Seat.Value,
            NorthPier.Value,
            HarborRoles.Operator.Value);
        var refusal = (await changing.Should().ThrowAsync<PostgresException>()).Which;
        refusal.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refusal.ConstraintName.Should().Be("tenancy_grant_is_fixed");
    }

    [Fact]
    public async Task Archiving_a_role_that_manages_access_takes_the_role_key_for_the_whole_tenant_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        const string archiving = "UPDATE tenancy.\"Roles\" SET \"Status\" = 'Archived' WHERE \"Id\" = $1";

        // The Grants desk manages access. Hiro holds it, and Seth gives roles too: neither archives it.
        foreach (var person in new[] { Hiro, Seth })
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, Harbor, Cancellation);
            (await caller.ExecuteAsync(archiving, Cancellation, GrantsDesk.Value)).Should().Be(0);
        }

        // The administrator does, and the right it gave Hiro goes with it.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync(archiving, Cancellation, GrantsDesk.Value)).Should().Be(1);
            await ada.CommitAsync(Cancellation);
        }

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"RoleId\" = $1", Cancellation, GrantsDesk.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Changing_a_roles_keys_takes_the_role_key_for_the_whole_tenant_by_a_query_as_the_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        const string adding = "UPDATE tenancy.\"Roles\" SET \"Keys\" = pg_catalog.array_append(\"Keys\", 'tenancy.roles.manage') WHERE \"Id\" = $1";

        // A Supervisor manages units, seats and grants, and no roles: the Operator's keys stay as they are.
        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await seth.ExecuteAsync(adding, Cancellation, HarborRoles.Operator.Value)).Should().Be(0);
        }

        // The administrator changes them, and every seat that holds the role holds the key with it.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync(adding, Cancellation, HarborRoles.Operator.Value)).Should().Be(1);
            await ada.CommitAsync(Cancellation);
        }

        await using var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation);
        (await oli.ScalarAsync<bool>("SELECT tenancy.holds_key('tenancy.roles.manage')", Cancellation)).Should().BeTrue();
    }
}

/// <summary>The policies on the grants of roles that manage access, under the names Entity Framework gives the tables and columns.</summary>
public sealed class RolesThatManageAccessTestsOnDefaultNames(TenancyPostgres postgres) : RolesThatManageAccessTests(postgres, TenancyNaming.Default);

/// <summary>The policies on the grants of roles that manage access, under snake_case names with enums stored as snake_case text.</summary>
public sealed class RolesThatManageAccessTestsOnSnakeCase(TenancyPostgres postgres) : RolesThatManageAccessTests(postgres, TenancyNaming.SnakeCase);
