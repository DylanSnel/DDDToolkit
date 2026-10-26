using System.Linq.Expressions;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The save check: whatever loaded or made a row, a save writes only the current caller's tenant's rows, and
/// nobody and system work outside any tenant write none; provisioning saves as system work in the tenant it makes.
/// It covers Tenancy's rows, the parts they own and a consumer's entities alike, and refuses a model that lost the
/// tenant filter.
/// </summary>
public abstract class SaveCheckTests(TestDatabases databases) : IAsyncLifetime
{
    private TestServices _services = null!;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Saving_a_row_of_another_tenant_is_refused_before_anything_is_written()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var orchard = await _services.ProvisionAsync("orchard");
        var roles = _services.Database.CountRows("Roles");

        // A new row of the other tenant.
        await using (var scope = _services.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
            var catalogue = scope.ServiceProvider.GetRequiredService<TenancyCatalogue>();

            using (TenancyCallers.Begin(HostCaller.InSeat(harbor.Tenant, harbor.AdminSeat)))
            {
                context.Add(TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
                    RoleId.CreateSequential(), orchard.Tenant, new RoleDraft("Stowaway", string.Empty, [HostCatalogue.WidgetRead]), catalogue));

                _services.Commands.Reset();
                await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
                _services.Commands.Commands.Should().BeEmpty("the check comes before the first command");
            }
        }

        // A row loaded in one tenant, and a part it owns, saved in another.
        await using (var scope = _services.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();

            HostSeat seat;
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
            {
                seat = await context.Set<HostSeat>().SingleAsync(TestContext.Current.CancellationToken);
            }

            seat.Rename("Renamed elsewhere");
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(orchard.Tenant))
            {
                _services.Commands.Reset();
                await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
                _services.Commands.Commands.Should().BeEmpty();
            }
        }

        // Only a part the organization owns changes. A context without the toolkit's interceptors, so nothing
        // marks the organization changed as well: the check reaches it through its unit.
        await using (var scope = _services.Scope())
        {
            var options = _services.Database.Options<TestTenancyContext>();
            options.UseTenancy(scope.ServiceProvider);
            await using var context = new TestTenancyContext(options.Options);

            HostOrganization organization;
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
            {
                organization = await context.Set<HostOrganization>().SingleAsync(TestContext.Current.CancellationToken);
            }

            organization.Root.SetCostCentre("NL-002");
            context.ChangeTracker.DetectChanges();
            context.Entry(organization).State.Should().Be(EntityState.Unchanged);

            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(orchard.Tenant))
            {
                await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
            }
        }

        _services.Database.CountRows("Roles").Should().Be(roles);
        await using (var scope = _services.Scope())
        {
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
            {
                var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
                (await context.Set<HostSeat>().SingleAsync(TestContext.Current.CancellationToken)).DisplayName.Should().Be("Ada");
                (await context.Set<HostOrganization>().SingleAsync(TestContext.Current.CancellationToken)).Root.CostCentre.Should().BeNull();
            }
        }
    }

    [Fact]
    public async Task Nobody_cannot_save_tenant_rows()
    {
        var harbor = await _services.ProvisionAsync("harbor");

        await using var scope = _services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        var catalogue = scope.ServiceProvider.GetRequiredService<TenancyCatalogue>();

        context.Add(TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
            RoleId.CreateSequential(), harbor.Tenant, new RoleDraft("Unclaimed", string.Empty, [HostCatalogue.WidgetRead]), catalogue));

        TenancyCallers.Ambient.Should().BeNull();
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.SeatSuspended)))
        {
            await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        _services.Database.CountRows("Roles").Should().Be(harbor.RolesByPack.Count);
    }

    [Fact]
    public async Task System_work_outside_any_tenant_saves_no_tenants_row()
    {
        var harbor = await _services.ProvisionAsync("harbor");

        await using var scope = _services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();
        var catalogue = scope.ServiceProvider.GetRequiredService<TenancyCatalogue>();

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            // A new tenant saved as provisioning once was, and a new row of an existing one: it acts in no tenant.
            context.Add(TenancyInstances.NewTenant<HostTenant, TenantId, SeatId>(new TenantId(99), TenantSlug.Create("stowaway").ToValid(), TenantShape.Flat));
            _services.Commands.Reset();
            await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
            _services.Commands.Commands.Should().BeEmpty("the check comes before the first command");

            context.ChangeTracker.Clear();
            context.Add(TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
                RoleId.CreateSequential(), harbor.Tenant, new RoleDraft("Unclaimed", string.Empty, [HostCatalogue.WidgetRead]), catalogue));
            await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));

            widgets.Widgets.Add(Widget(harbor.Tenant, "unasked"));
            await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => widgets.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        _services.Database.CountRows("Tenants").Should().Be(1);
        _services.Database.CountRows("Roles").Should().Be(harbor.RolesByPack.Count);
        _services.Database.CountRows("Widgets").Should().Be(0);
    }

    [Fact]
    public async Task Global_system_provisions()
    {
        var harbor = await _services.ProvisionAsync("harbor");

        _services.Database.CountRows("Tenants").Should().Be(1);
        _services.Database.CountRows("Organizations").Should().Be(1);
        _services.Database.CountRows("OrganizationUnits").Should().Be(1);
        _services.Database.CountRows("Seats").Should().Be(1);
        _services.Database.CountRows("SeatPlacements").Should().Be(1);
        _services.Database.CountRows("SeatRoleGrants").Should().Be(1);
        _services.Database.CountRows("Roles").Should().Be(harbor.RolesByPack.Count);
        _services.Database.CountRows("TenancyAccessRevisions").Should().Be(1);

        await using var scope = _services.Scope();
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var context = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
            var tenant = await context.Set<HostTenant>().SingleAsync(TestContext.Current.CancellationToken);
            tenant.Slug.Value.Should().Be("harbor");
            tenant.Status.Should().Be(TenantStatus.Active);

            var seat = await context.Set<HostSeat>().SingleAsync(TestContext.Current.CancellationToken);
            seat.Placements.Should().ContainSingle().Which.Grants.Should().ContainSingle().Which.RoleId.Should().Be(harbor.AdministratorRole);
        }
    }

    [Fact]
    public async Task A_consumer_entity_is_checked_too()
    {
        var harbor = new TenantId(1);
        var orchard = new TenantId(2);
        var seat = SeatId.CreateSequential();

        await using var scope = _services.Scope();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();

        using (TenancyCallers.Begin(HostCaller.InSeat(harbor, seat)))
        {
            widgets.Widgets.Add(Widget(orchard, "not ours"));
            await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => widgets.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        widgets.ChangeTracker.Clear();
        widgets.Widgets.Add(Widget(harbor, "ours"));
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => widgets.SaveChangesAsync(TestContext.Current.CancellationToken));

        using (TenancyCallers.Begin(HostCaller.InSeat(harbor, seat)))
        {
            await widgets.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _services.Database.CountRows("Widgets").Should().Be(1);
    }

    [Fact]
    public async Task A_type_derived_from_a_scoped_one_is_checked_as_its_root()
    {
        await using var scope = _services.Scope();
        var options = _services.Database.Options<ParcelContext>();
        options.UseDDDToolkit(scope.ServiceProvider).UseTenancy(scope.ServiceProvider);
        await using var parcels = new ParcelContext(options.Options);
        parcels.GetService<IRelationalDatabaseCreator>().CreateTables();

        var harbor = new TenantId(1);
        var orchard = new TenantId(2);

        // ScopeToTenant marks the root; the derived type shares its table and its filter, and so its check.
        parcels.Model.FindEntityType(typeof(ExpressParcel))!.GetRootType().ClrType.Should().Be<Parcel>();
        using (TenancyCallers.Begin(HostCaller.InSeat(harbor, SeatId.CreateSequential())))
        {
            parcels.Add(new ExpressParcel { Id = Guid.NewGuid(), TenantId = orchard, Courier = "Next door" });
            await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => parcels.SaveChangesAsync(TestContext.Current.CancellationToken));

            parcels.ChangeTracker.Clear();
            parcels.Add(new ExpressParcel { Id = Guid.NewGuid(), TenantId = harbor, Courier = "Ours" });
            await parcels.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _services.Database.CountRows("Parcels").Should().Be(1);
    }

    [Fact]
    public async Task A_scoped_type_without_the_named_filter_fails_on_first_save()
    {
        await using var scope = _services.Scope();
        var options = _services.Database.Options<UnfilteredWidgetContext>();
        options.UseDDDToolkit(scope.ServiceProvider).UseTenancy(scope.ServiceProvider);
        await using var widgets = new UnfilteredWidgetContext(options.Options);

        var tenant = new TenantId(1);
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
        {
            widgets.Widgets.Add(Widget(tenant, "unguarded"));

            var failure = await FluentActions.Awaiting(() => widgets.SaveChangesAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            failure.Which.Message.Should().Contain(TenancyQueryFilter.Name).And.Contain(nameof(Widget));
        }

        _services.Database.CountRows("Widgets").Should().Be(0);
    }

    private static Widget Widget(TenantId tenant, string name)
        => new() { Id = Guid.NewGuid(), TenantId = tenant, UnitId = OrganizationUnitId.CreateSequential(), Name = name };

    /// <summary>What a consumer keeps to a tenant, with a kind of its own in the same table.</summary>
    public class Parcel
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }
    }

    /// <summary>A parcel of a kind of its own: a type derived from the scoped one.</summary>
    public sealed class ExpressParcel : Parcel
    {
        public string? Courier { get; set; }
    }

    /// <summary>A consumer that keeps parcels to a tenant, the express ones among them.</summary>
    private sealed class ParcelContext(DbContextOptions<ParcelContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("parcels");
            modelBuilder.Entity<Parcel>(parcel =>
            {
                parcel.ToTable("Parcels");
                parcel.HasKey(row => row.Id);
                parcel.Property(row => row.Id).ValueGeneratedNever();
                parcel.ScopeToTenant(row => row.TenantId);
            });
            modelBuilder.Entity<ExpressParcel>();
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
        }
    }

    /// <summary>A consumer that keeps widgets to a tenant and then drops the tenant filter it was given.</summary>
    private sealed class UnfilteredWidgetContext(DbContextOptions<UnfilteredWidgetContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Widget>(widget =>
            {
                widget.ToTable("Widgets");
                widget.HasKey(row => row.Id);
                widget.ScopeToTenant(row => row.TenantId);
                widget.HasQueryFilter(TenancyQueryFilter.Name, (LambdaExpression?)null);
            });

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
        }
    }
}

/// <summary>The save check, on SQLite in memory.</summary>
public sealed class SaveCheckTestsOnSqlite() : SaveCheckTests(TestDatabases.Sqlite);

/// <summary>The save check, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class SaveCheckTestsOnPostgres(PostgresDatabases postgres) : SaveCheckTests(postgres);
