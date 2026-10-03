using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Who reads what of a tenant's access, by a query as the caller, past the use cases: a seat reads its own rights and
/// no other seat's, whatever it manages; a grant is read by its own seat, and by the seats that manage grants, seats
/// or units somewhere, or roles for the whole tenant; system work reads every right of its tenant; and the units, the
/// seats, the placements, the roles and the tree stay every member's to read.
/// </summary>
public abstract class RightsVisibilityTests(TenancyPostgres postgres, TenancyNaming names)
{
    private const string SeatsWithRights = "SELECT DISTINCT \"SeatId\" FROM tenancy.\"SeatRights\"";

    private const string SeatsWithGrants = "SELECT DISTINCT \"SeatId\" FROM tenancy.\"SeatRoleGrants\"";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_plain_member_reads_only_its_own_rights()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Oli operates widgets at North Pier and manages nothing: of all the rights of Harbor, he reads his three.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>(SeatsWithRights, Cancellation)).Should().Equal(Oli.Seat.Value);
            (await oli.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" ORDER BY 1", Cancellation)).Should().Equal("widget.change", "widget.create", "widget.read");
            (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Ada.Seat.Value)).Should().Be(0);
        }

        // In Orchard he reads the one his seat there holds, and none of Harbor's, his own included.
        await using (var inOrchard = await AsCaller.PersonAsync(database, Oli.Identity, Orchard, Cancellation))
        {
            (await inOrchard.ListAsync<Guid>(SeatsWithRights, Cancellation)).Should().Equal(OliInOrchard.Value);
        }

        // Eve's rights apply at no moment near now, and are hers to read all the same: when one applies is asked as it is read.
        await using (var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation))
        {
            (await eve.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\"", Cancellation)).Should().Be(4);
        }

        // Through Entity Framework, with the tenant filter skipped, it is the same three.
        await using var services = new TenancyServices(database);
        var read = await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Tenancy().Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>()
            .IgnoreQueryFilters()
            .Select(right => right.SeatId)
            .ToListAsync(Cancellation));
        read.Should().HaveCount(3).And.OnlyContain(seat => seat == Oli.Seat);
    }

    [Fact]
    public async Task A_plain_member_reads_only_its_own_grants()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>(SeatsWithGrants, Cancellation)).Should().Equal(Oli.Seat.Value);
            (await oli.ListAsync<Guid>("SELECT \"RoleId\" FROM tenancy.\"SeatRoleGrants\"", Cancellation)).Should().Equal(HarborRoles.Operator.Value);
        }

        // Eve holds nothing that applies now: her two grants, the one that ended and the one still to start, are hers to read.
        await using (var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation))
        {
            (await eve.ListAsync<Guid>(SeatsWithGrants, Cancellation)).Should().Equal(Eve.Seat.Value);
            (await eve.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRoleGrants\"", Cancellation)).Should().Be(2);
        }

        // A seat loaded through the store comes with the grants the caller may read: its own with all of them, another
        // seat's with none.
        await using var services = new TenancyServices(database);
        var (own, other) = await services.BySeat(Oli.Identity, Harbor, Oli.Seat, async scoped =>
        {
            var store = scoped.GetRequiredService<HostTenancy.IStore>();
            return ((await store.FindSeatAsync(Oli.Seat, Cancellation))!, (await store.FindSeatAsync(Seth.Seat, Cancellation))!);
        });
        own.Placements.SelectMany(placement => placement.Grants).Should().ContainSingle();
        other.Placements.Should().HaveCount(2, "where a seat is placed, every member reads");
        other.Placements.SelectMany(placement => placement.Grants).Should().BeEmpty();
    }

    [Fact]
    public async Task A_grants_seats_or_units_manager_somewhere_reads_every_grant_of_the_tenant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldAtAsync(database, Eve, North, [TenancyKeys.SeatsManage], Cancellation);
        await HoldAtAsync(database, Oli, NorthPier, [TenancyKeys.UnitsManage], Cancellation);
        var harbor = await GrantsOfAsync(database, Harbor);

        // Hiro gives roles at North, Eve manages seats there, and Oli units at North Pier: each reads the grants of every
        // seat of Harbor, at the root and at South too, where they manage nothing, and none of another tenant.
        foreach (var manager in new[] { Hiro, Eve, Oli })
        {
            await using var caller = await AsCaller.PersonAsync(database, manager.Identity, Harbor, Cancellation);
            (await caller.ListAsync<string>("SELECT \"SeatId\" || ' ' || \"UnitId\" || ' ' || \"RoleId\" FROM tenancy.\"SeatRoleGrants\"", Cancellation))
                .Should().BeEquivalentTo(harbor, "{0} manages access somewhere in Harbor", manager.Name);
        }

        // Which is what the use cases need: a seat is loaded whole, with every grant it has.
        await using var services = new TenancyServices(database);
        var seth = await services.BySeat(Eve.Identity, Harbor, Eve.Seat, scoped => scoped.GetRequiredService<HostTenancy.IStore>().FindSeatAsync(Seth.Seat, Cancellation));
        seth!.Placements.SelectMany(placement => placement.Grants).Should().ContainSingle().Which.RoleId.Should().Be(HarborRoles.Supervisor);

        // Acting in Orchard, where he only watches, Oli reads his own there.
        await using var inOrchard = await AsCaller.PersonAsync(database, Oli.Identity, Orchard, Cancellation);
        (await inOrchard.ListAsync<Guid>(SeatsWithGrants, Cancellation)).Should().Equal(OliInOrchard.Value);
    }

    [Fact]
    public async Task A_roles_manager_for_the_whole_tenant_reads_every_grant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldAtAsync(database, Eve, HarborRoot, [TenancyKeys.RolesManage], Cancellation);
        await HoldAtAsync(database, Oli, NorthPier, [TenancyKeys.RolesManage, HostCatalogue.WidgetRead], Cancellation);
        var harbor = await GrantsOfAsync(database, Harbor);

        // Changing a role reaches every seat that holds it, so whoever manages roles for the whole tenant reads them all.
        await using (var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation))
        {
            (await eve.ListAsync<string>("SELECT \"SeatId\" || ' ' || \"UnitId\" || ' ' || \"RoleId\" FROM tenancy.\"SeatRoleGrants\"", Cancellation)).Should().BeEquivalentTo(harbor);
        }

        // The role key held below the root changes no role, and reads no other seat's grants.
        await using var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation);
        (await oli.ListAsync<Guid>(SeatsWithGrants, Cancellation)).Should().Equal(Oli.Seat.Value);
    }

    [Fact]
    public async Task Nobody_reads_another_seats_rights_by_a_query_not_even_an_administrator()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Ada holds every key of Harbor, Seth manages units, seats and grants at North, Hiro gives roles there.
        foreach (var person in new[] { Ada, Seth, Hiro })
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, Harbor, Cancellation);
            (await caller.ListAsync<Guid>(SeatsWithRights, Cancellation)).Should().Equal([person.Seat.Value], "{0} reads no rights but their own", person.Name);
            (await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Oli.Seat.Value)).Should().Be(0);
        }

        // Nor through Entity Framework with the filters skipped, or with SQL of the application's own.
        await using var services = new TenancyServices(database);
        var (filtered, raw) = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped => (
            await scoped.Tenancy().Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().IgnoreQueryFilters().Select(right => right.SeatId).Distinct().ToListAsync(Cancellation),
            await scoped.Tenancy().Database.SqlQueryRaw<long>(names.Sql("SELECT count(*) AS \"Value\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" <> {0}"), Ada.Seat.Value).SingleAsync(Cancellation)));
        filtered.Should().Equal(Ada.Seat);
        raw.Should().Be(0);
    }

    [Fact]
    public async Task System_in_reads_every_right_of_its_tenant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        List<string> harbor;
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            harbor = await owner.ListAsync<string>("SELECT \"SeatId\" || ' ' || \"UnitId\" || ' ' || \"RoleId\" || ' ' || \"Key\" FROM tenancy.\"SeatRights\" WHERE \"TenantId\" = 1", Cancellation);
        }

        harbor.Should().HaveCountGreaterThan(20);

        // Tenancy's own system work and another module's alike: every seat's rights of the tenant it acts in, and no
        // other tenant's.
        foreach (var scope in new[] { TenancyWork.SystemScope, "widgets" })
        {
            await using var system = await AsCaller.SystemInAsync(database, Harbor, scope, Cancellation);
            (await system.ListAsync<string>("SELECT \"SeatId\" || ' ' || \"UnitId\" || ' ' || \"RoleId\" || ' ' || \"Key\" FROM tenancy.\"SeatRights\"", Cancellation))
                .Should().BeEquivalentTo(harbor, "system work in the scope {0} reads its tenant's rights", scope);
            (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRoleGrants\"", Cancellation)).Should().Be(7, "and its grants");
        }

        // Outside any tenant it reads none.
        await using var nowhere = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation);
        (await nowhere.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\"", Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Units_seats_placements_roles_and_the_tree_stay_readable_tenant_wide()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);

        // Oli operates widgets at North Pier and manages nothing, and reads every row of these of Harbor, of every seat
        // and every unit: who sits where, and what each role holds.
        await using var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation);
        foreach (var table in new[] { "OrganizationUnits", "OrganizationUnitPaths", "Seats", "SeatPlacements", "Roles", "TenancyAccessRevisions" })
        {
            var harbor = await owner.ScalarAsync<long>($"SELECT count(*) FROM tenancy.\"{table}\" WHERE \"TenantId\" = 1", Cancellation);
            harbor.Should().BePositive();
            (await oli.ScalarAsync<long>($"SELECT count(*) FROM tenancy.\"{table}\" WHERE \"TenantId\" = 1", Cancellation)).Should().Be(harbor, table + " is read in full");
            (await oli.ScalarAsync<long>($"SELECT count(*) FROM tenancy.\"{table}\" WHERE \"TenantId\" <> 1 AND NOT (\"{(table == "Seats" ? "Identity" : "TenantId")}\" = $1)", Cancellation, table == "Seats" ? Oli.Identity : (object)0L))
                .Should().Be(0, table + " shows no other tenant's rows");
        }

        // Of Orchard, where Oli has a seat too, the directory shows the tenant and his own seat, and nothing more.
        (await oli.ListAsync<Guid>("SELECT \"Id\" FROM tenancy.\"Seats\" WHERE \"TenantId\" = 2", Cancellation)).Should().Equal(OliInOrchard.Value);
        (await oli.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Roles\" WHERE \"TenantId\" = 2", Cancellation)).Should().Be(0);
    }

    /// <summary>Every grant of a tenant, as its seat, unit and role, as the owner reads them.</summary>
    private static async Task<List<string>> GrantsOfAsync(TestDatabase database, TenantId tenant)
    {
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        var grants = await owner.ListAsync<string>(
            "SELECT g.\"SeatId\" || ' ' || g.\"UnitId\" || ' ' || g.\"RoleId\" FROM tenancy.\"SeatRoleGrants\" g JOIN tenancy.\"Seats\" s ON s.\"Id\" = g.\"SeatId\" WHERE s.\"TenantId\" = $1",
            Cancellation,
            tenant.Value);
        grants.Should().HaveCountGreaterThan(7);
        return grants;
    }
}

/// <summary>Who reads what of a tenant's access, under the names Entity Framework gives the tables and columns.</summary>
public sealed class RightsVisibilityTestsOnDefaultNames(TenancyPostgres postgres) : RightsVisibilityTests(postgres, TenancyNaming.Default);

/// <summary>Who reads what of a tenant's access, under snake_case names with enums stored as snake_case text.</summary>
public sealed class RightsVisibilityTestsOnSnakeCase(TenancyPostgres postgres) : RightsVisibilityTests(postgres, TenancyNaming.SnakeCase);
