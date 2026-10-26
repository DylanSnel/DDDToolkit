using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The second lock, as the docs describe it. What it stops: the application's own queries that forget a condition, run
/// as a seat through Entity Framework, a filter skipped, raw SQL, a bulk update or delete, still reach only what the
/// seat may, in its tenant, read no other seat's rights, write none at all, and cannot remove the last administrator. What it does not stop: SQL that switches the
/// role on the application's connection, which escapes every policy.
/// </summary>
public abstract class ThreatModelTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_query_that_skips_the_tenant_filter_still_reads_only_its_tenant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var secured = new TenancyServices(database);
        await using var unsecured = new TenancyServices(database, rowLevelSecurity: false);

        // Oli, in Harbor, runs the application's code with the filters skipped, and with SQL of its own.
        async Task<(int Roles, int Units, long Raw, int Widgets)> ReadAsync(TenancyServices services)
            => await services.BySeat(Oli.Identity, Harbor, Oli.Seat, async scoped => (
                await scoped.Tenancy().Set<HostRole>().IgnoreQueryFilters().CountAsync(Cancellation),
                await scoped.Tenancy().Set<OrganizationUnitRow<TenantId, OrganizationUnitId>>().IgnoreQueryFilters().CountAsync(Cancellation),
                await scoped.Tenancy().Database.SqlQueryRaw<long>(names.Sql("SELECT count(*) AS \"Value\" FROM tenancy.\"SeatRights\"")).SingleAsync(Cancellation),
                await scoped.Widgets().Widgets.IgnoreQueryFilters().CountAsync(Cancellation)));

        var owner = await ReadAsync(unsecured);
        owner.Roles.Should().Be(13, "past the policies, the forgotten filter reaches every tenant");
        owner.Widgets.Should().Be(4);

        var seat = await ReadAsync(secured);
        seat.Roles.Should().Be(5, "Harbor's roles alone");
        seat.Units.Should().Be(4, "Harbor's units alone");
        seat.Raw.Should().Be(3, "his own rights alone, the three his Operator role gives him");
        owner.Raw.Should().BeGreaterThan(await HarborRightsAsync(database), "past the policies, every seat's, of every tenant");
        seat.Widgets.Should().Be(1, "the widgets his rule lets him read, in Harbor");
    }

    [Fact]
    public async Task A_bulk_update_or_delete_changes_only_what_the_seat_may()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Oli manages no seats: renaming every seat renames his own.
        var renamed = await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Tenancy().Set<HostSeat>()
            .IgnoreQueryFilters()
            .ExecuteUpdateAsync(seats => seats.SetProperty(seat => seat.DisplayName, "Renamed"), Cancellation));
        renamed.Should().Be(1);

        // Ada, who holds every key of Harbor, takes away every right Oli holds, in any tenant: none goes. No caller
        // writes a right.
        var deleted = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Tenancy().Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>()
            .IgnoreQueryFilters()
            .Where(right => right.SeatId == Oli.Seat || right.SeatId == OliInOrchard)
            .ExecuteDeleteAsync(Cancellation));

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>("SELECT \"DisplayName\" FROM tenancy.\"Seats\" WHERE \"DisplayName\" = 'Renamed'", Cancellation)).Should().ContainSingle();
        deleted.Should().Be(0);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Oli.Seat.Value)).Should().Be(3, "the three rights of Oli's Operator role at North Pier stay");
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, OliInOrchard.Value)).Should().Be(1, "and so does Orchard's");

        // Hiro, who gives roles at North, takes away every grant there is: Oli's at North Pier goes, which he may take,
        // and his own, and the rights they gave with them. Every other grant stays, Seth's at North too, whose keys he
        // does not hold.
        var taken = await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Tenancy().Database
            .ExecuteSqlRawAsync(names.Sql("DELETE FROM tenancy.\"SeatRoleGrants\""), Cancellation));

        taken.Should().Be(2);
        (await owner.ListAsync<Guid>("SELECT DISTINCT \"SeatId\" FROM tenancy.\"SeatRoleGrants\" g JOIN tenancy.\"Seats\" s ON s.\"Id\" = g.\"SeatId\" WHERE s.\"TenantId\" = 1", Cancellation))
            .Should().BeEquivalentTo([Ada.Seat.Value, Seth.Seat.Value, Sue.Seat.Value, Eve.Seat.Value]);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = ANY ($1)", Cancellation, new[] { Oli.Seat.Value, Hiro.Seat.Value }))
            .Should().Be(0, "the rights of the grants that went, went with them");
    }

    [Fact]
    public async Task A_bulk_update_cannot_remove_the_last_administrator()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Ada stops every seat of Harbor at once, her own included: the policies let a seats manager change them, and
        // the rights of each go with its status, the last administrator's too.
        var removing = () => services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Tenancy().Set<HostSeat>()
            .ExecuteUpdateAsync(seats => seats.SetProperty(seat => seat.Status, SeatStatus.Suspended), Cancellation));

        var failure = await removing.Should().ThrowAsync<Exception>();
        (failure.Which as PostgresException ?? failure.Which.InnerException as PostgresException)!.ConstraintName.Should().Be("tenancy_administrator_remains");

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"TenantId\" = 1 AND \"Key\" = $1", Cancellation, TenancyKeys.AdministratorKey))
            .Should().Be(1, "Ada is still Harbor's administrator");
    }

    [Fact]
    public async Task Sql_that_switches_the_role_on_the_applications_connection_escapes_every_policy()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Postgres checks SET ROLE against the role that logged in, not the one in use: any statement on the application's
        // connection can go back to the owner, or become system work in any tenant. Parameterized queries are what keep
        // such a statement off the connection.
        await using var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation);
        (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Roles\"", Cancellation)).Should().Be(5);

        await oli.ExecuteAsync("RESET ROLE", Cancellation);
        (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Roles\"", Cancellation)).Should().Be(13, "as the owner, every tenant's");

        await oli.ExecuteAsync("SELECT set_config('role', 'ddd_system_in', true), set_config('tenancy.caller_tenant', '2', true), set_config('request.jwt.claims', '{\"scope\":\"tenancy\"}', true)", Cancellation);
        (await oli.ListAsync<string>("SELECT \"DisplayName\" FROM tenancy.\"Seats\" ORDER BY 1", Cancellation)).Should().BeEquivalentTo("Odette", "Oli");
    }

    /// <summary>How many rights Harbor has, as the owner counts them.</summary>
    private static async Task<long> HarborRightsAsync(TestDatabase database)
    {
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        return await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"TenantId\" = 1", Cancellation);
    }
}

/// <summary>What the second lock stops and what it does not, under the names Entity Framework gives the tables and columns.</summary>
public sealed class ThreatModelTestsOnDefaultNames(TenancyPostgres postgres) : ThreatModelTests(postgres, TenancyNaming.Default);

/// <summary>What the second lock stops and what it does not, under snake_case names with enums stored as snake_case text.</summary>
public sealed class ThreatModelTestsOnSnakeCase(TenancyPostgres postgres) : ThreatModelTests(postgres, TenancyNaming.SnakeCase);
