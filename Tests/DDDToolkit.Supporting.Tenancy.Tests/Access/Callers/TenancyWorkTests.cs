using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// System work in Tenancy begins two callers in one scope: Tenancy's, which says in which tenant the work acts, and
/// the toolkit's scoped system caller, which says what kind of work it is. Never the toolkit's own system caller,
/// which stands for the application past every policy: provisioning narrows itself to the tenant it makes, and the
/// few reads across tenants belong to the storage package, each named, which begins them in no tenant at all.
/// </summary>
public class TenancyWorkTests
{
    private static readonly TenantId Harbor = new(1);
    private static readonly SeatId Ada = SeatId.CreateSequential();

    [Fact]
    public void BeginSystem_begins_the_core_scoped_system_caller_with_no_tenant()
    {
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            var core = Callers.Ambient!;
            core.Kind.Should().Be(CallerKind.SystemIn);
            core.Scope.Should().Be(TenancyWork.SystemScope).And.Be("tenancy");
            core.IsSystem.Should().BeFalse("the scoped system caller is not the application past every policy");

            TenancyCallers.Ambient!.Kind.Should().Be(TenancyCallerKind.System);
            TenancyCallers.CurrentTenantOrNull().Should().BeNull("system work outside any tenant has no tenant to act in");
        }

        Callers.Ambient.Should().BeNull();
        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public void BeginSystemIn_begins_both_callers_and_disposing_ends_both()
    {
        var person = Caller.User(Guid.NewGuid());
        var seat = HostCaller.InSeat(Harbor, Ada);

        using (Callers.Begin(person))
        using (TenancyCallers.Begin(seat))
        {
            var work = TenancyWork.BeginSystemIn(Harbor, (SeatId?)Ada);

            Callers.Ambient!.IsSystemIn.Should().BeTrue();
            Callers.Ambient.Scope.Should().Be(TenancyWork.SystemScope);
            TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.SystemIn(Harbor, Ada));

            work.Dispose();
            Callers.Ambient.Should().BeSameAs(person, "the toolkit's caller before it is back");
            TenancyCallers.Ambient.Should().BeSameAs(seat, "and so is Tenancy's");

            work.Dispose();
            Callers.Ambient.Should().BeSameAs(person, "disposing twice ends nothing more");
            TenancyCallers.Ambient.Should().BeSameAs(seat);
        }

        Callers.Ambient.Should().BeNull();
        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public void BeginNone_leaves_no_tenancy_caller_until_it_is_disposed()
    {
        var seat = HostCaller.InSeat(Harbor, Ada);

        using (TenancyCallers.Begin(seat))
        {
            var none = TenancyCallers.BeginNone();

            // What runs inside acts in no tenant, as it would outside any scope: nobody, to whoever asks.
            TenancyCallers.Ambient.Should().BeNull();
            TenancyCallers.CurrentTenantOrNull().Should().BeNull("the tenant of the seat around it does not travel inside");
            TenancyCallers.Current<TenantId, SeatId>().Kind.Should().Be(TenancyCallerKind.Nobody);

            // System work begun inside it is itself, and ends back at none.
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor))
            {
                TenancyCallers.CurrentTenantOrNull().Should().Be(Harbor);
            }

            TenancyCallers.Ambient.Should().BeNull();

            none.Dispose();
            TenancyCallers.Ambient.Should().BeSameAs(seat, "the caller before it is back");

            none.Dispose();
            TenancyCallers.Ambient.Should().BeSameAs(seat, "disposing twice ends nothing more");
        }

        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public void BeginSystemIn_carries_its_scope_to_the_core_caller()
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor, scope: "projects"))
        {
            Callers.Ambient!.Scope.Should().Be("projects", "a module's own work in a tenant says whose it is");
            TenancyCallers.CurrentTenantOrNull().Should().Be(Harbor, "the scope says whose work it is, not where it acts");
        }

        // A scope that is not one begins neither caller.
        FluentActions.Invoking(() => TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor, scope: "Projects!")).Should().Throw<ArgumentException>();
        Callers.Ambient.Should().BeNull();
        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public async Task Provisioning_runs_inside_the_new_tenant()
    {
        var harness = new Harness(New.Catalogue());

        HostTenancy.ProvisionedTenant provisioned;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            harness.Store.BeginUnitOfWork();
            provisioned = await harness.Tenants.ProvisionAsync(
                new HostTenancy.TenantToProvision("harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", Guid.NewGuid()),
                TestContext.Current.CancellationToken);
        }

        // Every read and the save: system work in the new tenant, in Tenancy's own scope.
        var inTheNewTenant = HostCaller.SystemIn(provisioned.Tenant);
        harness.Store.Observed.Select(call => call.Name).Should().Contain([nameof(InMemoryTenancyStore.SlugTakenAsync), nameof(InMemoryTenancyStore.SaveAsync)]);
        harness.Store.Observed.Should().OnlyContain(
            call => Equals(call.Tenancy, inTheNewTenant) && call.Core != null && call.Core.IsSystemIn && call.Core.Scope == TenancyWork.SystemScope,
            "provisioning sees and writes only the tenant it makes");
    }

    [Fact]
    public async Task Nothing_in_Tenancy_begins_the_core_System_caller_but_the_listed_reads()
    {
        // Every use case, as the application runs them: system work begun with TenancyWork, a person as their seat.
        var harness = new Harness(New.Catalogue());
        HostTenancy.ProvisionedTenant harbor;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            harness.Store.BeginUnitOfWork();
            harbor = await harness.Tenants.ProvisionAsync(
                new HostTenancy.TenantToProvision("harbor", "Harbor Works", TenantShape.Flat, "Harbor Works", Guid.NewGuid()),
                TestContext.Current.CancellationToken);
        }

        var tenant = harbor.Tenant;
        var root = harbor.RootUnit;
        var watchers = harbor.RolesByPack[HostCatalogue.WatcherPack];

        await BySystemIn(harness, tenant, h => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, default));
        await BySystemIn(harness, tenant, h => h.Tenants.RenameOrganizationAsync("Harbor Works Ltd", default));
        var north = await BySystemIn(harness, tenant, h => h.Organization.AddUnitAsync(root, "North", default));
        var south = await BySystemIn(harness, tenant, h => h.Organization.AddUnitAsync(root, "South", default));
        var coast = await BySystemIn(harness, tenant, h => h.Organization.AddUnitAsync(north, "Coast", default));
        await BySystemIn(harness, tenant, h => h.Organization.RenameUnitAsync(coast, "North Coast", default));
        await BySystemIn(harness, tenant, h => h.Organization.MoveUnitAsync(coast, south, default));

        var grace = await BySystemIn(harness, tenant, h => h.Seats.AddSeatAsync(Guid.NewGuid(), default, configure: seat => seat.Rename("Grace")));
        await BySystemIn(harness, tenant, h => h.Seats.PlaceAsync(grace, north, primary: true, default));
        await BySystemIn(harness, tenant, h => h.Seats.PlaceAsync(grace, south, primary: false, default));
        await BySystemIn(harness, tenant, h => h.Seats.MakePrimaryAsync(grace, south, default));
        await BySystemIn(harness, tenant, h => h.Seats.GrantAsync(grace, south, watchers, until: null, reason: null, default));

        var clerks = await BySystemIn(harness, tenant, h => h.Roles.CreateAsync("Clerk", "Files widgets", [HostCatalogue.WidgetCreate], default));
        await BySystemIn(harness, tenant, h => h.Roles.RenameAsync(clerks, "Filing clerk", "Files widgets", default));
        await BySystemIn(harness, tenant, h => h.Roles.SetKeysAsync(clerks, [HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead], default));
        await BySystemIn(harness, tenant, h => h.Seats.GrantAsync(grace, south, clerks, until: null, reason: null, default));

        // A person, as their seat: the directory and a command.
        var administrator = HostCaller.InSeat(tenant, harbor.AdminSeat);
        using (Callers.Begin(Caller.User(Guid.NewGuid())))
        {
            await harness.Run(administrator, h => h.Directory.WhoAmIAsync(default));
            await harness.Run(administrator, h => h.Directory.ListSeatsAsync(default));
            await harness.Run(administrator, h => h.Directory.ListRolesAsync(default));
            await harness.Run(administrator, h => h.Directory.ListUnitsAsync(default));
            await harness.Run(administrator, h => h.Seats.RevokeAsync(grace, south, clerks, default));
        }

        await BySystemIn(harness, tenant, h => h.Roles.ArchiveAsync(clerks, default));
        await BySystemIn(harness, tenant, h => h.Seats.WithdrawAsync(grace, north, default));
        await BySystemIn(harness, tenant, h => h.Seats.SuspendAsync(grace, default));
        await BySystemIn(harness, tenant, h => h.Seats.ReactivateAsync(grace, default));
        await BySystemIn(harness, tenant, h => h.Seats.DeactivateAsync(grace, default));
        await BySystemIn(harness, tenant, h => h.Organization.ArchiveUnitAsync(coast, default));
        await BySystemIn(harness, tenant, h => h.Tenants.SuspendAsync("Unpaid invoices", default));
        await BySystemIn(harness, tenant, h => h.Tenants.ReactivateAsync(default));
        await BySystemIn(harness, tenant, h => h.Tenants.CloseAsync("Moved away", default));

        // The reads across tenants that run as the toolkit's System are the storage package's, each on a context
        // of its own; none of them goes through this store, so here nothing is exempt.
        string[] listedReads = [];
        harness.Store.Observed.Should().HaveCountGreaterThan(50, "the use cases above all went through the store");
        harness.Store.Observed
            .Where(call => !listedReads.Contains(call.Name))
            .Should().NotContain(
                call => call.Core != null && (call.Core.IsSystem || ReferenceEquals(call.Core, Caller.System)),
                "nothing in Tenancy runs as the application past every policy");
    }

    /// <summary>Runs <paramref name="act"/> as system work in <paramref name="tenant"/>, begun as an application begins it, in a new unit of work.</summary>
    private static async Task<T> BySystemIn<T>(Harness harness, TenantId tenant, Func<Harness, Task<T>> act)
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
        {
            harness.Store.BeginUnitOfWork();
            return await act(harness);
        }
    }

    /// <summary>Runs <paramref name="act"/> as system work in <paramref name="tenant"/>, begun as an application begins it, in a new unit of work.</summary>
    private static Task BySystemIn(Harness harness, TenantId tenant, Func<Harness, Task> act)
        => BySystemIn(harness, tenant, async h =>
        {
            await act(h);
            return true;
        });
}
