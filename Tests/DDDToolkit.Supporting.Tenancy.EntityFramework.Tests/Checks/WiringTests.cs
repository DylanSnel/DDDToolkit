using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// A context whose saves Tenancy does not check, because it was given the toolkit's base alone,
/// <c>UseDDDToolkitCore</c>, without <c>UseTenancy</c>, or because <c>UseTenancy</c> came before the toolkit's
/// interceptors, fails loudly instead of writing what nobody checked. <c>UseDDDToolkit</c> alone wires it.
/// </summary>
public sealed class WiringTests
{
    [Fact]
    public async Task The_base_alone_without_UseTenancy_fails_loudly_on_save()
    {
        using var services = new TestServices(Wiring.WithoutTenancy);

        var failure = await FluentActions.Awaiting(() => services.ProvisionAsync("harbor")).Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("has no TenancySaveInterceptor").And.Contain("UseTenancy");

        services.Database.CountRows("Tenants").Should().Be(0);
    }

    [Fact]
    public async Task UseTenancy_before_the_toolkits_interceptors_fails_loudly()
    {
        using var services = new TestServices(Wiring.TenancyFirst);

        var failure = await FluentActions.Awaiting(() => services.ProvisionAsync("harbor")).Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("before the DDDToolkit interceptors");

        services.Database.CountRows("Tenants").Should().Be(0);
    }

    [Fact]
    public async Task EnsureWired_passes_for_a_wired_consumer_context()
    {
        using var services = new TestServices();
        await using var scope = services.Scope();

        FluentActions.Invoking(() => TenancyChecks.EnsureWired(scope.ServiceProvider.GetRequiredService<TestWidgetContext>())).Should().NotThrow();
        FluentActions.Invoking(() => TenancyChecks.EnsureWired(scope.ServiceProvider.GetRequiredService<TestTenancyContext>())).Should().NotThrow();

        // The same consumer given the base alone, without Tenancy's interceptor: options, not the context's type, decide.
        var unwired = new DbContextOptionsBuilder<TestWidgetContext>().UseSqlite(services.Sqlite.Connection);
        unwired.UseDDDToolkitCore(scope.ServiceProvider);
        await using var widgets = new TestWidgetContext(unwired.Options);
        FluentActions.Invoking(() => TenancyChecks.EnsureWired(widgets)).Should().Throw<InvalidOperationException>()
            .WithMessage("*" + nameof(TestWidgetContext) + "*UseTenancy*");
    }

    [Fact]
    public async Task An_accessor_registered_after_tenancy_fails_loudly_where_the_history_is_kept()
    {
        // The container answers the last registration: the host's accessor takes the place of Tenancy's, which
        // would have wrapped it had it come first, and every row of the history would name the person's identity
        // where it should name the seat.
        using var services = new TestServices(afterTenancy: registered => registered.AddSingleton<IActedByAccessor, TheHostsOwn>());
        services.Provider.GetRequiredService<IActedByAccessor>().Should().BeOfType<TheHostsOwn>();

        const string says =
            "'TestTenancyContext' keeps Tenancy's access history, and its rows would not say who acted as Tenancy knows the caller: " +
            "IActedByAccessor is TheHostsOwn, registered after AddTenancy in place of TenancyActedByAccessor. " +
            "Register the application's own accessor before AddTenancy, as a singleton: Tenancy then answers for its callers and asks that one for everybody else.";

        await using (var scope = services.Scope())
        {
            FluentActions.Invoking(() => TenancyChecks.EnsureWired(scope.ServiceProvider.GetRequiredService<TestTenancyContext>()))
                .Should().Throw<InvalidOperationException>().WithMessage(says);
            FluentActions.Invoking(() => TenancyChecks.EnsureWired(scope.ServiceProvider.GetRequiredService<TestWidgetContext>()))
                .Should().NotThrow("the widgets' context keeps no history, so who acted is not its question");
        }

        // The store asks before every save, so nothing is written under the wrong name.
        (await FluentActions.Awaiting(() => services.ProvisionAsync("harbor")).Should().ThrowAsync<InvalidOperationException>()).WithMessage(says);
        services.Database.CountRows("Tenants").Should().Be(0);
        services.Database.CountRows("EventLog").Should().Be(0);

        // One registered per scope after Tenancy cannot even be asked of the application's own services; a scope of
        // them says which it is.
        using var scoped = new TestServices(afterTenancy: registered => registered.AddScoped<IActedByAccessor, TheHostsOwn>());
        (await FluentActions.Awaiting(() => scoped.ProvisionAsync("harbor")).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*IActedByAccessor is TheHostsOwn, registered after AddTenancy as a scoped service in place of TenancyActedByAccessor. Register the application's own accessor before AddTenancy, as a singleton*");
        scoped.Database.CountRows("Tenants").Should().Be(0);

        // Taken away after Tenancy, nothing says who acted at all.
        using var removed = new TestServices(afterTenancy: registered => registered.RemoveAll<IActedByAccessor>());
        (await FluentActions.Awaiting(() => removed.ProvisionAsync("harbor")).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*IActedByAccessor is not registered any more: TenancyActedByAccessor was taken away after AddTenancy. Register the application's own accessor before AddTenancy*");

        // Registered first, the same accessor is the one Tenancy asks for everybody it does not know.
        using var first = new TestServices(configure: registered => registered.AddSingleton<IActedByAccessor, TheHostsOwn>());
        (await first.ProvisionAsync("harbor")).Tenant.Should().NotBe(default(TenantId));
    }

    [Fact]
    public async Task An_accessor_the_container_cannot_make_fails_with_the_containers_own_reason()
    {
        // Registered where it belongs, before Tenancy, but it takes a service nobody registered. That is not an
        // accessor in the wrong place, and the check does not say it is: what the container says is the reason.
        using var services = new TestServices(configure: registered => registered.AddSingleton<IActedByAccessor, NeedsWhatNobodyRegistered>());
        await using var scope = services.Scope();

        FluentActions.Invoking(() => TenancyChecks.EnsureWired(scope.ServiceProvider.GetRequiredService<TestTenancyContext>()))
            .Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(nameof(NeverRegistered)).And.Contain(nameof(NeedsWhatNobodyRegistered)).And.NotContain("AddTenancy");
    }

    /// <summary>An accessor of the host's own, which answers one kind whoever is calling.</summary>
    private sealed class TheHostsOwn : IActedByAccessor
    {
        public ActedBy Current => new("host", null);
    }

    /// <summary>A service no test registers.</summary>
    private sealed class NeverRegistered;

    /// <summary>An accessor the container cannot make: it takes a service that is not registered.</summary>
    private sealed class NeedsWhatNobodyRegistered(NeverRegistered missing) : IActedByAccessor
    {
        public ActedBy Current => new(missing.ToString()!, null);
    }
}
