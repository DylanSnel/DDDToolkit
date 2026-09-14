using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests.Invariants;

/// <summary>
/// The guarantee that the seam is called: every aggregate root a save writes is asked to prove it is
/// consistent, and a root that is not stops the transaction.
/// </summary>
public sealed class InvariantInterceptorTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ServiceProvider _provider;

    public InvariantInterceptorTests()
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddDbContext<TabContext>((sp, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(sp));
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TabContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _db.Dispose();
    }

    /// <summary>A context with the toolkit's interceptors, in its own scope.</summary>
    private async Task InScopeAsync(Func<TabContext, Task> action)
    {
        using var scope = _provider.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<TabContext>());
    }

    /// <summary>A context with no interceptors at all, for seeding rows the rule would refuse.</summary>
    private TabContext Unchecked() => new(_db.Options<TabContext>());

    [Fact]
    public async Task An_aggregate_with_no_invariants_saves_exactly_as_before()
    {
        var id = TabId.CreateUnique();

        await InScopeAsync(async context =>
        {
            context.QuietTabs.Add(new QuietTab(id, "nothing to prove"));
            await context.SaveChangesAsync();
        });

        await InScopeAsync(async context =>
        {
            var tab = await context.QuietTabs.SingleAsync(t => t.Id == id);
            tab.Version.Should().Be(1);
            tab.Renote("still nothing");
            await context.SaveChangesAsync();
            tab.Version.Should().Be(2);
        });

        await InScopeAsync(async context =>
            (await context.QuietTabs.SingleAsync(t => t.Id == id)).Note.Should().Be("still nothing"));
    }

    [Fact]
    public async Task A_satisfied_invariant_saves()
    {
        var id = TabId.CreateUnique();

        await InScopeAsync(async context =>
        {
            var tab = new Tab(id, limit: 5000);
            tab.Order("Negroni", 1200);
            tab.Order("Martini", 1400);
            context.Tabs.Add(tab);
            await context.SaveChangesAsync();

            tab.Checks.Should().Be(1, "the seam ran exactly once for this save");
            tab.Version.Should().Be(1);
        });

        await InScopeAsync(async context =>
        {
            var tab = await context.Tabs.SingleAsync(t => t.Id == id);
            tab.Total.Should().Be(2600);
        });
    }

    [Fact]
    public async Task A_violated_invariant_throws_naming_the_aggregate_and_writes_nothing()
    {
        var id = TabId.CreateUnique();
        var tab = new Tab(id, limit: 1000);
        tab.Order("Champagne", 9000);

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        context.Tabs.Add(tab);

        var act = () => context.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<InvariantViolationException>()).Which;
        exception.AggregateType.Should().Be<Tab>();
        exception.AggregateId.Should().Be(id);
        exception.Message.Should().Contain("Tab").And.Contain(id.ToString()).And.Contain("may not exceed its limit");

        _db.CountRows("Tabs").Should().Be(0, "the transaction never started");
        tab.Version.Should().Be(0, "a rejected save leaves the version alone");
    }

    [Fact]
    public void The_synchronous_path_checks_the_same_way()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var tab = new Tab(TabId.CreateUnique(), limit: 100);
        tab.Order("Cognac", 5000);
        context.Tabs.Add(tab);

        var act = () => context.SaveChanges();

        act.Should().Throw<InvariantViolationException>();
        _db.CountRows("Tabs").Should().Be(0);
    }

    [Fact]
    public async Task A_violation_caused_by_a_change_to_an_owned_child_is_caught_through_its_root()
    {
        var id = TabId.CreateUnique();

        await InScopeAsync(async context =>
        {
            var tab = new Tab(id, limit: 5000);
            tab.Order("House red", 800);
            context.Tabs.Add(tab);
            await context.SaveChangesAsync();
        });

        using var scope = _provider.CreateScope();
        var checkedContext = scope.ServiceProvider.GetRequiredService<TabContext>();
        var loaded = await checkedContext.Tabs.SingleAsync(t => t.Id == id, TestContext.Current.CancellationToken);

        // The root itself is untouched: only the child changes.
        loaded.Lines[0].Reprice(9999);

        var act = () => checkedContext.SaveChangesAsync();

        (await act.Should().ThrowAsync<InvariantViolationException>()).Which.AggregateType.Should().Be<Tab>();

        await InScopeAsync(async context =>
        {
            var reloaded = await context.Tabs.SingleAsync(t => t.Id == id);
            reloaded.Total.Should().Be(800, "the rejected save wrote nothing");
        });
    }

    [Fact]
    public async Task The_seam_is_not_called_on_an_unchanged_aggregate()
    {
        var untouched = TabId.CreateUnique();
        var touched = TabId.CreateUnique();

        // Seed a tab that already breaks its rule, through a context with no interceptors.
        await using (var seeding = Unchecked())
        {
            var broken = new Tab(untouched, limit: 1);
            broken.Order("Bottle service", 100000);
            seeding.Tabs.Add(broken);
            await seeding.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();

        var loadedBroken = await context.Tabs.SingleAsync(t => t.Id == untouched, TestContext.Current.CancellationToken);
        var fresh = new Tab(touched, limit: 5000);
        fresh.Order("Lemonade", 300);
        context.Tabs.Add(fresh);

        // The broken tab is tracked but unchanged, so it is never asked.
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        loadedBroken.Checks.Should().Be(0, "nothing about it changed, so it is not this save's problem");
        fresh.Checks.Should().Be(1);

        // A save with nothing changed at all asks nobody.
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        fresh.Checks.Should().Be(1, "the second save had nothing to write");

        // Touch it, and now it has to answer for itself.
        loadedBroken.RaiseLimit(2);
        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvariantViolationException>();
        loadedBroken.Checks.Should().Be(1);
    }

    [Fact]
    public void The_check_can_be_run_by_hand_against_a_context()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var tab = new Tab(TabId.CreateUnique(), limit: 100);
        context.Tabs.Add(tab);

        var withinLimit = () => InvariantInterceptor.CheckInvariants(context);
        withinLimit.Should().NotThrow();

        tab.Order("Armagnac", 20000);

        var overLimit = () => InvariantInterceptor.CheckInvariants(context);
        overLimit.Should().Throw<InvariantViolationException>();

        _db.CountRows("Tabs").Should().Be(0, "checking is not saving");
    }
}
