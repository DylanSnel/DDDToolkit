using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests.Invariants;

/// <summary>
/// The guarantee that the seam is called: every entity a save writes is asked to prove it is
/// consistent, child entities as well as their roots, and anything that is not stops the
/// transaction. What the save never does is read a navigation to find those children, and each of
/// them answers for itself alone, so nothing is reported twice by the root that holds it.
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
    public async Task A_child_answers_for_its_own_rule_when_nothing_but_the_child_changed()
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
        var line = loaded.Lines[0];

        // The tab stays far inside its limit, so the only rule that breaks is the line's own.
        line.Reprice(0);

        var act = () => checkedContext.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<InvariantViolationException>()).Which;
        exception.AggregateType.Should().Be<TabLine>("the line answers for itself, not through its tab");
        exception.AggregateId.Should().Be(line.Id);
        line.Checks.Should().Be(1);
        loaded.Checks.Should().Be(1, "the root is asked as well, because a rule of its own may span its lines");

        await InScopeAsync(async context =>
            (await context.Tabs.SingleAsync(t => t.Id == id)).Lines[0].Price.Should().Be(800, "the rejected save wrote nothing"));
    }

    [Fact]
    public async Task A_child_added_to_a_tab_that_was_already_saved_answers_too()
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
        var comped = loaded.Order("Tap water", 0);

        var act = () => checkedContext.SaveChangesAsync();

        (await act.Should().ThrowAsync<InvariantViolationException>()).Which.AggregateId.Should().Be(comped.Id);
        comped.Checks.Should().Be(1);
        _db.CountRows("TabLine").Should().Be(1, "the free drink was never written");
    }

    [Fact]
    public async Task A_child_on_its_way_out_is_not_asked()
    {
        var id = TabId.CreateUnique();
        TabLineId comped;

        // Seed a line that breaks its own rule, through a context with no interceptors.
        await using (var seeding = Unchecked())
        {
            var tab = new Tab(id, limit: 5000);
            tab.Order("Negroni", 1200);
            comped = tab.Order("Tap water", 0).Id;
            seeding.Tabs.Add(tab);
            await seeding.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var loaded = await context.Tabs.SingleAsync(t => t.Id == id, TestContext.Current.CancellationToken);
        var line = loaded.Lines.Single(l => l.Id == comped);

        loaded.Remove(comped).Should().BeTrue();

        // Deleting it is how you get rid of a broken line, so this save may not refuse it.
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        line.Checks.Should().Be(0, "a line on its way out has no state left to be consistent about");
        _db.CountRows("TabLine").Should().Be(1);
    }

    [Fact]
    public async Task An_unloaded_navigation_is_never_touched()
    {
        var id = CellarId.CreateUnique();

        // Seed, through a context with no interceptors, a cellar whose bottles break the child's rule.
        await using (var seeding = Unchecked())
        {
            var cellar = new Cellar(id, "Cave Nord");
            cellar.Stock("Barolo", -3);
            seeding.Cellars.Add(cellar);
            await seeding.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var loaded = await context.Cellars.SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
        var bottles = context.Entry(loaded).Collection(nameof(Cellar.Bottles));
        bottles.IsLoaded.Should().BeFalse("the cellar was queried without them");

        loaded.Rename("Cave Sud");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        loaded.Reads.Should().Be(0, "the save worked from change tracker entries and never read the navigation");
        bottles.IsLoaded.Should().BeFalse("so nothing lazily loaded the bottles either");

        // The bottles that were left alone really are broken, so the save above passed on merit, and
        // reading the navigation really does move the counter that stayed at zero.
        await InScopeAsync(async other =>
        {
            var reloaded = await other.Cellars.Include(c => c.Bottles).SingleAsync(c => c.Id == id);
            var readsBefore = reloaded.Reads;

            var check = () => reloaded.Bottles[0].EnsureInvariants();

            check.Should().Throw<InvariantViolationException>();
            reloaded.Reads.Should().Be(readsBefore + 1);
        });
    }

    [Fact]
    public async Task The_whole_unit_of_work_can_be_asked_what_is_broken_without_throwing()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var cellar = new Cellar(CellarId.CreateUnique(), "Cave Nord");
        var barolo = cellar.Stock("Barolo", 6);
        context.Cellars.Add(cellar);

        InvariantInterceptor.GetInvariantViolations(context).Should().BeEmpty("nothing is broken yet");

        cellar.Rename(string.Empty);
        barolo.Take(10);

        var violations = InvariantInterceptor.GetInvariantViolations(context);

        violations.Select(violation => violation.Code).Should().Equal(
            Cellar.MustHaveAName,
            Bottle.MustNotGoNegative);
        violations[1].Message.Should().Contain("Barolo");
        _db.CountRows("Cellars").Should().Be(0, "asking is not saving");

        // The same context still refuses to save, which is the stage where broken is not an answer.
        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvariantViolationException>();
    }

    [Fact]
    public async Task A_broken_child_is_reported_once_and_not_a_second_time_through_its_root()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var cellar = new Cellar(CellarId.CreateUnique(), string.Empty);
        cellar.Stock("Barolo", -3);
        context.Cellars.Add(cellar);

        // What the domain answers about this aggregate: its own broken name and its broken bottle.
        // The save has both the cellar and the bottle on its list, so a save that asked either of them
        // this question would hear about the bottle twice.
        cellar.GetInvariantViolations().Should().HaveCount(2, "the walk answers for the whole aggregate");

        var violations = InvariantInterceptor.GetInvariantViolations(context);

        violations.Select(violation => violation.Code).Should().Equal(
            Cellar.MustHaveAName,
            Bottle.MustNotGoNegative);
        violations.Should().ContainSingle(violation => violation.Code == Bottle.MustNotGoNegative,
            "the bottle answers for itself, and no cellar repeats it");

        var act = () => context.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<InvariantViolationException>()).Which;
        exception.AggregateType.Should().Be<Cellar>("the cellar is asked before the bottle it holds");
        exception.Violations.Should().ContainSingle("it answers for its own name only, never for the bottle");
        exception.Message.Should().NotContain("Barolo");
    }

    [Fact]
    public async Task A_save_that_only_a_child_breaks_throws_naming_the_child()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var cellar = new Cellar(CellarId.CreateUnique(), "Cave Nord");
        var barolo = cellar.Stock("Barolo", -3);
        context.Cellars.Add(cellar);

        var act = () => context.SaveChangesAsync();

        // Had the save walked, the cellar would have answered first and in its own name for a rule
        // that belongs to the bottle.
        var exception = (await act.Should().ThrowAsync<InvariantViolationException>()).Which;
        exception.AggregateType.Should().Be<Bottle>();
        exception.AggregateId.Should().Be(barolo.Id);
        exception.Violations.Should().ContainSingle();

        InvariantInterceptor.GetInvariantViolations(context).Should()
            .ContainSingle("one broken bottle is one violation, however many objects hold it");
    }

    [Fact]
    public void Asking_what_is_broken_answers_where_the_save_would_throw()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TabContext>();
        var tab = new Tab(TabId.CreateUnique(), limit: 100);
        tab.Order("Armagnac", 20000);
        context.Tabs.Add(tab);

        var ask = () => InvariantInterceptor.GetInvariantViolations(context);
        var insist = () => InvariantInterceptor.CheckInvariants(context);

        ask.Should().NotThrow("the check stage answers with a list even where the aggregate's seam throws");
        ask().Should().Contain(violation => violation.Message.Contains("may not exceed its limit"));
        insist.Should().Throw<InvariantViolationException>("while the save stage insists");
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
