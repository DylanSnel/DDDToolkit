using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The directory answers what seats, roles and units are called, by id, through the use cases as the caller and
/// under the policies: from Tenancy's own tables, since the functions a module reads through answer no name. A seat
/// that manages nothing reads the names of its whole tenant, and no name of another, its own seat there included.
/// </summary>
public abstract class DirectoryTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_seat_reads_the_names_of_its_tenant_by_id()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder));

        // Oli operates widgets at North Pier and manages nothing: no key is asked for a name.
        await services.BySeat(Oli.Identity, Harbor, Oli.Seat, async scoped =>
        {
            var directory = scoped.Directory();

            recorder.Clear();
            (await directory.SeatsByIdAsync([Sue.Seat, Ada.Seat, Seth.Seat], Cancellation)).Should().Equal(
                new HostTenancy.SeatSummary(Ada.Seat, "Ada", SeatStatus.Active),
                new HostTenancy.SeatSummary(Seth.Seat, "Seth", SeatStatus.Active),
                new HostTenancy.SeatSummary(Sue.Seat, "Sue", SeatStatus.Suspended));
            var seats = recorder.Sent.Should().ContainSingle("the seats asked for are one statement").Which.Text;
            seats.Should().Contain(names.Of("Seats")).And.NotContain(TenancyFunctionNames.TenantSeats, "a name is read from the seats' own table, which the policies let a seat of the tenant read");

            recorder.Clear();
            (await directory.RolesByIdAsync([HarborRoles.Watcher, GrantsDesk], Cancellation)).Select(role => (role.Id, role.Name, role.ManagesAccess)).Should().Equal(
                (GrantsDesk, "Grants desk", true),
                (HarborRoles.Watcher, "Watcher", false));
            recorder.Sent.Should().ContainSingle("a tenant's roles are one statement");

            // South is no unit Oli is placed under, and the root is above him: both are his tenant's, and named.
            recorder.Clear();
            (await directory.UnitsByIdAsync([South, HarborRoot, NorthPier], Cancellation)).Select(unit => (unit.Id, unit.Name, unit.Path, unit.Depth)).Should().Equal(
                (HarborRoot, "Harbor", "Harbor", 1),
                (NorthPier, "North Pier", "Harbor / North / North Pier", 3),
                (South, "South", "Harbor / South", 2));
            recorder.Sent.Should().HaveCount(2, "the organization with its units, and the closure that orders a path");

            (await directory.ListUnitsAsync(Cancellation)).Select(unit => unit.Path).Should().Equal(["Harbor / North / North Pier"], "the list is still the units he is placed under");
            (await directory.ListSeatsAsync(Cancellation)).Select(seat => seat.DisplayName).Should().Equal("Ada", "Eve", "Hiro", "Oli", "Seth", "Sue");
            (await directory.ListRolesAsync(Cancellation)).Select(role => role.Name).Should().Equal("Administrator", "Grants desk", "Operator", "Supervisor", "Watcher");
        });
    }

    [Fact]
    public async Task Another_tenants_ids_are_left_out()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Oli has a seat in Orchard too, which the database lets him read wherever he signs in: asked in Harbor, it is
        // no seat of the tenant he works in, and is left out like any other tenant's.
        await services.BySeat(Oli.Identity, Harbor, Oli.Seat, async scoped =>
        {
            (await scoped.Tenancy().Set<HostSeat>().IgnoreQueryFilters().Select(seat => seat.Id).ToListAsync(Cancellation))
                .Should().Contain(OliInOrchard, "with the tenant filter skipped, the policies show a person every seat of their own");

            var directory = scoped.Directory();
            (await directory.SeatsByIdAsync([OliInOrchard, Odette.Seat, Quin.Seat, Oli.Seat], Cancellation)).Select(seat => seat.Id).Should().Equal(Oli.Seat);
            (await directory.RolesByIdAsync([OrchardRoles.Watcher, QuayRoles.Administrator, HarborRoles.Watcher], Cancellation)).Select(role => role.Id).Should().Equal(HarborRoles.Watcher);
            (await directory.UnitsByIdAsync([OrchardRoot, QuayRoot, North], Cancellation)).Select(unit => unit.Id).Should().Equal(North);

            (await directory.SeatsByIdAsync([OliInOrchard, Odette.Seat], Cancellation)).Should().BeEmpty("nothing says whether an id is another tenant's or nobody's");
        });

        // And the same person in Orchard reads Orchard's names, and none of Harbor's.
        await services.BySeat(Oli.Identity, Orchard, OliInOrchard, async scoped =>
        {
            var directory = scoped.Directory();
            (await directory.SeatsByIdAsync([OliInOrchard, Odette.Seat, Oli.Seat, Ada.Seat], Cancellation)).Select(seat => (seat.Id, seat.DisplayName))
                .Should().Equal((Odette.Seat, "Odette"), (OliInOrchard, "Oli"));
            (await directory.UnitsByIdAsync([OrchardRoot, HarborRoot], Cancellation)).Select(unit => unit.Path).Should().Equal("Orchard");
        });

        // System work in a tenant reads that tenant's names, and no other's.
        await services.BySystemIn(Harbor, async scoped =>
        {
            (await scoped.Directory().SeatsByIdAsync([Ada.Seat, Odette.Seat, OliInOrchard], Cancellation)).Select(seat => seat.DisplayName).Should().Equal("Ada");
            (await scoped.Directory().UnitsByIdAsync([NorthPier, OrchardRoot], Cancellation)).Select(unit => unit.Path).Should().Equal("Harbor / North / North Pier");
        });
    }
}

/// <summary>The directory's names by id, under the names Entity Framework gives the tables and columns.</summary>
public sealed class DirectoryTestsOnDefaultNames(TenancyPostgres postgres) : DirectoryTests(postgres, TenancyNaming.Default);

/// <summary>The directory's names by id, under snake_case names with enums stored as snake_case text.</summary>
public sealed class DirectoryTestsOnSnakeCase(TenancyPostgres postgres) : DirectoryTests(postgres, TenancyNaming.SnakeCase);
