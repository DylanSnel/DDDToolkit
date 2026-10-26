using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Contexts taken from a pool save as any other does. A pool builds a context's options once, with the application's
/// services, and hands one instance to one caller after another, so nothing of a save may come from the options:
/// the save check reads the caller of each save, the rights are written by the save that changed them, and the
/// events go to the outbox, whoever rented the instance before.
/// </summary>
public sealed class PooledContextTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_save_check_and_the_rights_are_written_through_a_pooled_context()
    {
        using var services = new TestServices(pooled: true);
        var harbor = await services.ProvisionAsync("harbor");
        var orchard = await services.ProvisionAsync("orchard");
        var north = await services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var operators = harbor.RolesByPack[HostCatalogue.OperatorPack];

        // Every command in a scope of its own, whose context is one of the pool's.
        var rented = new List<DbContext>();
        Task<T> AsHarborsAdministrator<T>(Func<IServiceProvider, Task<T>> act) => services.BySeat(harbor.Tenant, harbor.AdminSeat, scoped =>
        {
            var context = scoped.Tenancy();
            context.IsPooled().Should().BeTrue();
            FluentActions.Invoking(() => TenancyChecks.EnsureWired(context)).Should().NotThrow("a pooled context is wired like any other");
            rented.Add(context);
            return act(scoped);
        });

        // A seat added, placed and given a role by Harbor's administrator, through the use cases.
        var grace = await AsHarborsAdministrator(scoped => scoped.Seats().AddSeatAsync(Guid.NewGuid(), Cancellation, configure: seat => seat.Rename("Grace")));
        await AsHarborsAdministrator(async scoped =>
        {
            await scoped.Seats().PlaceAsync(grace, north, primary: true, Cancellation);
            return true;
        });
        await AsHarborsAdministrator(async scoped =>
        {
            await scoped.Seats().GrantAsync(grace, north, operators, until: null, reason: null, Cancellation);
            return true;
        });

        rented.Should().HaveCount(3);
        rented.Distinct().Should().ContainSingle("each command was handed the context the one before gave back");

        // The save that gave the role wrote the rights it gives, in Tenancy's own context.
        var rights = await services.StoredRightsAsync(grace);
        rights.Should().NotBeEmpty().And.OnlyContain(right => right.TenantId == harbor.Tenant && right.UnitId == north && right.RoleId == operators);
        rights.Select(right => right.Key).Should().Contain([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate]);

        // And each save stored its events with what it changed.
        await using (var scope = services.Scope())
        {
            var stored = await scope.ServiceProvider.Tenancy().Set<OutboxMessage>()
                .Select(message => new { message.EventName, message.AggregateId })
                .ToListAsync(Cancellation);
            stored.Where(message => message.AggregateId!.Contains(grace.Value.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(message => message.EventName)
                .Should().BeEquivalentTo("tenancy.seat-added", "tenancy.seat-placed", "tenancy.organization-role-granted");
        }

        // The save check reads the caller of each save. Orchard's administrator saves a widget of Orchard's, and
        // gives the context back; Harbor's, handed that very instance, is refused a widget of Orchard's on it.
        var widgets = new List<DbContext>();
        await services.BySeat(orchard.Tenant, orchard.AdminSeat, async scoped =>
        {
            widgets.Add(scoped.Widgets());
            scoped.Widgets().Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = orchard.Tenant, UnitId = orchard.RootUnit, Name = "Crate" });
            await scoped.Widgets().SaveChangesAsync(Cancellation);
        });

        await services.BySeat(harbor.Tenant, harbor.AdminSeat, async scoped =>
        {
            var context = scoped.Widgets();
            widgets.Add(context);
            context.ChangeTracker.Entries().Should().BeEmpty("what the renter before tracked left with it");

            context.Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = orchard.Tenant, UnitId = orchard.RootUnit, Name = "Stowaway" });
            services.Commands.Reset();
            await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => context.SaveChangesAsync(Cancellation));
            services.Commands.Commands.Should().BeEmpty("the check comes before the first command");
        });

        // Her own tenant's row she saves, on the same instance once more.
        await services.BySeat(harbor.Tenant, harbor.AdminSeat, async scoped =>
        {
            widgets.Add(scoped.Widgets());
            scoped.Widgets().Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = harbor.Tenant, UnitId = north, Name = "Pump" });
            await scoped.Widgets().SaveChangesAsync(Cancellation);
        });

        widgets.Distinct().Should().ContainSingle("each caller was handed the context the one before gave back");
        services.Database.CountRows("Widgets").Should().Be(2, "the widget of Orchard's and the one of Harbor's, and no stowaway");
    }

    [Fact]
    public async Task A_read_across_tenants_takes_a_context_of_its_own_from_the_pool()
    {
        using var services = new TestServices(pooled: true);
        var harbor = await services.ProvisionAsync("harbor");
        var orchard = await services.ProvisionAsync("orchard");

        // The read makes a scope of its own from the application's services, and asks it for the context. Under a
        // pool those are the services the pool's options were built with, and the context is the one AddScopedFromPool
        // binds to that scope: a second one of the pool, while the caller holds its own.
        await using var scope = services.Scope();
        var asking = scope.ServiceProvider.Tenancy();

        services.Commands.Reset();
        var tenants = await TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(asking, "widgets", Cancellation);

        tenants.Should().BeEquivalentTo([harbor.Tenant, orchard.Tenant]);
        var read = services.Commands.Sent.Should().ContainSingle("the read is one query").Which;
        read.Context.Should().NotBeNull().And.NotBeSameAs(asking, "it runs on a context of its own");

        // Given back when the read ended: the next context taken from the pool is that one.
        await using var next = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestTenancyContext>>().CreateDbContextAsync(Cancellation);
        next.Should().BeSameAs(read.Context);
    }

    [Fact]
    public async Task A_read_across_tenants_says_how_to_register_a_context_it_can_make()
    {
        // A database with a tenant in it, made by services of the usual kind.
        using var database = new SqliteDatabase();
        HostTenancy.ProvisionedTenant harbor;
        using (var usual = new TestServices(database: database))
        {
            harbor = await usual.ProvisionAsync("harbor");
        }

        // A context made with new: its options name no services of the application, so the read has none to make its
        // own context from.
        await using (var made = new TestTenancyContext(database.Options<TestTenancyContext>().Options))
        {
            await FluentActions.Awaiting(() => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(made, "widgets", Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("'TestTenancyContext' was not made by the application's services*AddDbContext<TestTenancyContext>*AddScopedFromPool<TestTenancyContext>()*");
        }

        // A pool a host made by hand, which registers the factory and no context: the read finds the application's
        // services, and they cannot make it a context of that type.
        ServiceCollection WithAPoolMadeByHand()
        {
            var services = new ServiceCollection();
            services.AddDDDToolkitEntityFramework();
            TestHostTenancy.Add(services);
            services.AddSingleton<IDbContextFactory<TestTenancyContext>>(root =>
            {
                var options = database.Options<TestTenancyContext>();
                options.UseApplicationServiceProvider(root).UseDDDToolkit(root).UseTenancy(root);
                return new PooledDbContextFactory<TestTenancyContext>(options.Options);
            });
            return services;
        }

        await using (var provider = WithAPoolMadeByHand().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        await using (var taken = await provider.GetRequiredService<IDbContextFactory<TestTenancyContext>>().CreateDbContextAsync(Cancellation))
        {
            await FluentActions.Awaiting(() => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(taken, "widgets", Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("No 'TestTenancyContext' is registered under its own type*AddDbContext<TestTenancyContext>*AddScopedFromPool<TestTenancyContext>()*");
        }

        // What the message names is what it takes: the scope's own context, taken from that pool.
        var registered = WithAPoolMadeByHand();
        registered.AddScopedFromPool<TestTenancyContext>();
        await using (var provider = registered.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        await using (var taken = await provider.GetRequiredService<IDbContextFactory<TestTenancyContext>>().CreateDbContextAsync(Cancellation))
        {
            (await TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(taken, "widgets", Cancellation)).Should().Equal(harbor.Tenant);
        }
    }
}
