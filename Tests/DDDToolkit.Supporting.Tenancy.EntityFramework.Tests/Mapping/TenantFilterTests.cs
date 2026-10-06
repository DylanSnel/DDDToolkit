using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The tenant filter: every read of a tenant's rows keeps to the current caller's tenant, whatever the query was
/// compiled for, reads nothing for nobody and for system work outside any tenant, sits next to an application's
/// own filter, and is skipped by the seat directory for one identity only.
/// </summary>
public abstract class TenantFilterTests(TestDatabases databases) : IAsyncLifetime
{
    private TestServices _services = null!;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_tenant_filter_is_evaluated_per_query_not_per_model()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        await _services.AddSeatAsync(harbor.Tenant, Guid.NewGuid(), "Grace");
        var orchard = await _services.ProvisionAsync("orchard");

        // One context and fresh ones, one model: each execution reads the tenant of the caller running it.
        await using var scope = _services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();

        var counts = new List<int>();
        foreach (var tenant in new[] { harbor.Tenant, orchard.Tenant, harbor.Tenant, orchard.Tenant })
        {
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
            {
                counts.Add(await context.Set<HostSeat>().CountAsync(TestContext.Current.CancellationToken));

                await using var fresh = _services.Scope();
                (await fresh.ServiceProvider.GetRequiredService<TestTenancyContext>().Set<HostSeat>().CountAsync(TestContext.Current.CancellationToken))
                    .Should().Be(counts[^1]);
            }
        }

        counts.Should().Equal(2, 1, 2, 1);

        using (TenancyCallers.Begin(HostCaller.InSeat(orchard.Tenant, orchard.AdminSeat)))
        {
            (await context.Set<HostTenant>().Select(tenant => tenant.Id).ToListAsync(TestContext.Current.CancellationToken))
                .Should().Equal([orchard.Tenant], "a seat reads its own tenant, like system work in it");
        }
    }

    [Fact]
    public async Task Global_system_and_nobody_read_no_tenant_rows()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        await _services.ProvisionAsync("orchard");

        await using var scope = _services.Scope();
        var tenancy = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var counts = await CountEveryScopedSetAsync(tenancy, widgets);
            counts.Should().OnlyContain(count => count.Value > 0, "the tenant has a row of each");

            // The placements name no tenant of their own; their table keeps the seat's, and the views read it.
            _services.Database.CountRows("SeatPlacements").Should().Be(2);
            counts["placement view"].Should().Be(1, "the other tenant's administrator is placed too, and not seen");
            counts["consumer placement view"].Should().Be(1);
        }

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            (await CountEveryScopedSetAsync(tenancy, widgets)).Should().OnlyContain(count => count.Value == 0, "system work outside any tenant reads nothing inside one");
        }

        TenancyCallers.Ambient.Should().BeNull();
        (await CountEveryScopedSetAsync(tenancy, widgets)).Should().OnlyContain(count => count.Value == 0, "without a caller there is nobody");

        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.TenantInactive)))
        {
            (await CountEveryScopedSetAsync(tenancy, widgets)).Should().OnlyContain(count => count.Value == 0, "nobody is nobody, whatever the reason");
        }
    }

    [Fact]
    public async Task A_host_filter_on_the_same_entity_keeps_the_tenant_filter()
    {
        var harbor = new TenantId(1);
        var orchard = new TenantId(2);
        var unit = OrganizationUnitId.CreateSequential();

        await using var scope = _services.Scope();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor))
        {
            widgets.Widgets.AddRange(Widget(harbor, unit, "kept"), Widget(harbor, unit, "binned", deleted: true));
            await widgets.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(orchard))
        {
            widgets.Widgets.Add(Widget(orchard, unit, "next door"));
            await widgets.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor))
        {
            (await Names(widgets.Widgets)).Should().Equal("kept");
            (await Names(widgets.Widgets.IgnoreQueryFilters([TestWidgetContext.SoftDelete]))).Should().Equal(["binned", "kept"], "skipping the soft delete keeps the tenant filter");
            (await Names(widgets.Widgets.IgnoreQueryFilters([TenancyQueryFilter.Name]))).Should().Equal(["kept", "next door"], "skipping the tenant filter keeps the soft delete");
        }

        static async Task<List<string>> Names(IQueryable<Widget> widgets)
            => await widgets.OrderBy(widget => widget.Name).Select(widget => widget.Name).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_seat_directory_finds_seats_across_the_filter_by_identity_only()
    {
        var person = Guid.NewGuid();
        var harbor = await _services.ProvisionAsync("harbor", administrator: person);
        var orchard = await _services.ProvisionAsync("orchard");
        var inOrchard = await _services.AddSeatAsync(orchard.Tenant, person, "Ada at the orchard");
        await _services.AddSeatAsync(orchard.Tenant, Guid.NewGuid(), "Grace");

        await using var scope = _services.Scope();
        var directory = scope.ServiceProvider.GetRequiredService<ISeatDirectory<TenantId, SeatId>>();

        // Nobody is calling yet: finding the caller's seat is what selects a tenant.
        TenancyCallers.Ambient.Should().BeNull();

        var all = await directory.AllOfAsync<HostSeat>(person, TestContext.Current.CancellationToken);
        all.Select(seat => (seat.Slug, seat.Seat.Id)).Should().BeEquivalentTo([("harbor", harbor.AdminSeat), ("orchard", inOrchard)]);

        var found = await directory.FindAsync(person, "orchard", TestContext.Current.CancellationToken);
        found.Should().Be(new SeatOfCaller<TenantId, SeatId>(
            orchard.Tenant, "orchard", "Orchard Works", TenantStatus.Active, inOrchard, SeatStatus.Active));

        (await directory.FindAsync(person, "quarry", TestContext.Current.CancellationToken)).Should().BeNull("there is no such tenant");
        (await directory.FindAsync(Guid.NewGuid(), "harbor", TestContext.Current.CancellationToken)).Should().BeNull("a tenant is not found for someone without a seat in it");
        (await directory.AllOfAsync<HostSeat>(Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_seat_directory_answers_the_hosts_own_seats_of_the_identity_in_one_statement_and_tracks_none()
    {
        var person = Guid.NewGuid();
        var harbor = await _services.ProvisionAsync("harbor", administrator: person);
        var orchard = await _services.ProvisionAsync("orchard");
        var inOrchard = await _services.AddSeatAsync(orchard.Tenant, person, "Ada at the orchard");
        await _services.AddSeatAsync(orchard.Tenant, Guid.NewGuid(), "Grace");

        await using var scope = _services.Scope();
        var directory = scope.ServiceProvider.GetRequiredService<ISeatDirectory<TenantId, SeatId>>();

        // The name each tenant keeps for the person, a field of the host's own seat class, beside what the picker shows
        // of the tenant: the seats and their tenants are one statement.
        _services.Commands.Reset();
        var mine = await directory.AllOfAsync<HostSeat>(person, TestContext.Current.CancellationToken);

        mine.Select(found => (found.Slug, found.OrganizationName, found.TenantStatus, found.Seat.Id, found.Seat.DisplayName, found.Seat.Identity)).Should().BeEquivalentTo(
        [
            ("harbor", "Harbor Works", TenantStatus.Active, harbor.AdminSeat, "Ada", person),
            ("orchard", "Orchard Works", TenantStatus.Active, inOrchard, "Ada at the orchard", person),
        ]);
        _services.Commands.Count.Should().Be(1, "the seats are read with the tenants they are found in");

        mine[0].Seat.Rename("changed by the application");
        scope.ServiceProvider.Tenancy().ChangeTracker.Entries<HostSeat>().Should().BeEmpty("nothing done to a seat it answered is saved");

        // A class the seats derive from is a seat as well; a class they are not is refused before anything is read.
        (await directory.AllOfAsync<SeatAggregate<SeatId, TenantId, OrganizationUnitId, RoleId>>(person, TestContext.Current.CancellationToken))
            .Select(found => found.Seat.Id).Should().BeEquivalentTo([harbor.AdminSeat, inOrchard]);
        await FluentActions.Awaiting(() => directory.AllOfAsync<HostTenant>(person, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*HostSeat*HostTenant*");
    }

    /// <summary>Counts every set of both contexts that is kept to a tenant, by type.</summary>
    private static async Task<Dictionary<string, int>> CountEveryScopedSetAsync(TestTenancyContext tenancy, TestWidgetContext widgets)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return new Dictionary<string, int>
        {
            ["tenants"] = await tenancy.Set<HostTenant>().CountAsync(cancellationToken),
            ["organizations"] = await tenancy.Set<HostOrganization>().CountAsync(cancellationToken),
            ["seats"] = await tenancy.Set<HostSeat>().CountAsync(cancellationToken),
            ["roles"] = await tenancy.Set<HostRole>().CountAsync(cancellationToken),
            ["access revisions"] = await tenancy.Set<TenancyAccessRevision<TenantId>>().CountAsync(cancellationToken),
            ["rights"] = await tenancy.Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().CountAsync(cancellationToken),
            ["paths"] = await tenancy.Set<OrganizationUnitPath<TenantId, OrganizationUnitId>>().CountAsync(cancellationToken),
            ["unit view"] = await tenancy.Set<OrganizationUnitRow<TenantId, OrganizationUnitId>>().CountAsync(cancellationToken),
            ["role view"] = await tenancy.Set<RoleRow<TenantId, RoleId>>().CountAsync(cancellationToken),
            ["seat view"] = await tenancy.Set<SeatRow<TenantId, SeatId>>().CountAsync(cancellationToken),
            ["placement view"] = await tenancy.Set<PlacementRow<SeatId, OrganizationUnitId>>().CountAsync(cancellationToken),
            ["consumer rights view"] = await widgets.Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().CountAsync(cancellationToken),
            ["consumer paths view"] = await widgets.Set<OrganizationUnitPath<TenantId, OrganizationUnitId>>().CountAsync(cancellationToken),
            ["consumer unit view"] = await widgets.Set<OrganizationUnitRow<TenantId, OrganizationUnitId>>().CountAsync(cancellationToken),
            ["consumer role view"] = await widgets.Set<RoleRow<TenantId, RoleId>>().CountAsync(cancellationToken),
            ["consumer seat view"] = await widgets.Set<SeatRow<TenantId, SeatId>>().CountAsync(cancellationToken),
            ["consumer placement view"] = await widgets.Set<PlacementRow<SeatId, OrganizationUnitId>>().CountAsync(cancellationToken),
        };
    }

    private static Widget Widget(TenantId tenant, OrganizationUnitId unit, string name, bool deleted = false)
        => new() { Id = Guid.NewGuid(), TenantId = tenant, UnitId = unit, Name = name, IsDeleted = deleted };
}

/// <summary>The tenant filter, on SQLite in memory.</summary>
public sealed class TenantFilterTestsOnSqlite() : TenantFilterTests(TestDatabases.Sqlite);

/// <summary>The tenant filter, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class TenantFilterTestsOnPostgres(PostgresDatabases postgres) : TenantFilterTests(postgres);
