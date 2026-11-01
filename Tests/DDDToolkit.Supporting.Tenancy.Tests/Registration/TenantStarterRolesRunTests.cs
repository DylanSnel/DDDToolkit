using DDDToolkit.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Running another module's starter roles in a tenant: each in a scope of its own, added up, and one that fails does not
/// stop the others. Tenancy knows nothing of what each one does.
/// </summary>
public class TenantStarterRolesRunTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nothing_registered_does_nothing()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        var run = await TenantStarterRolesRun.RunAsync(services.GetRequiredService<IServiceScopeFactory>(), Cancellation);

        run.Should().BeEquivalentTo(new { RolesMade = 0, RolesChanged = 0, RolesWithoutTheirStarterRole = 0, Failed = Array.Empty<Exception>() });
    }

    [Fact]
    public async Task Each_one_runs_in_a_scope_of_its_own_and_what_they_did_is_added_up()
    {
        var scopes = new List<ScopeMark>();
        await using var services = new ServiceCollection()
            .AddSingleton(scopes)
            .AddScoped<ScopeMark>()
            .AddScoped<ITenantStarterRoles>(provider => new Counting(provider, new TenantStarterRolesFollowed(1, 2, 3)))
            .AddScoped<ITenantStarterRoles>(provider => new Counting(provider, new TenantStarterRolesFollowed(10, 20, 30)))
            .BuildServiceProvider();

        var run = await TenantStarterRolesRun.RunAsync(services.GetRequiredService<IServiceScopeFactory>(), Cancellation);

        (run.RolesMade, run.RolesChanged, run.RolesWithoutTheirStarterRole).Should().Be((11, 22, 33));
        run.Failed.Should().BeEmpty();
        scopes.Should().HaveCount(2).And.OnlyHaveUniqueItems("a module's context is the work's own");
    }

    [Fact]
    public async Task One_that_fails_is_kept_and_the_others_go_on()
    {
        var failure = new InvalidOperationException("the module is away");
        await using var services = new ServiceCollection()
            .AddSingleton(new List<ScopeMark>())
            .AddScoped<ScopeMark>()
            .AddScoped<ITenantStarterRoles>(_ => new Failing(failure))
            .AddScoped<ITenantStarterRoles>(provider => new Counting(provider, new TenantStarterRolesFollowed(1, 0, 0)))
            .BuildServiceProvider();

        var run = await TenantStarterRolesRun.RunAsync(services.GetRequiredService<IServiceScopeFactory>(), Cancellation);

        run.Failed.Should().Equal(failure);
        run.RolesMade.Should().Be(1);
    }

    [Fact]
    public async Task One_that_cannot_be_made_is_a_failure_and_ends_the_run()
    {
        var failure = new InvalidOperationException("not registered right");
        await using var services = new ServiceCollection()
            .AddScoped<ITenantStarterRoles>(_ => throw failure)
            .BuildServiceProvider();

        var run = await TenantStarterRolesRun.RunAsync(services.GetRequiredService<IServiceScopeFactory>(), Cancellation);

        run.Failed.Should().Equal(failure);
    }

    [Fact]
    public async Task A_row_another_run_changed_is_read_again_up_to_three_times()
    {
        var losing = new LosingTheRace(losses: 2);
        await using var services = new ServiceCollection().AddSingleton<ITenantStarterRoles>(losing).BuildServiceProvider();

        var run = await TenantStarterRolesRun.RunAsync(services.GetRequiredService<IServiceScopeFactory>(), Cancellation);

        (losing.Runs, run.RolesChanged, run.Failed.Count).Should().Be((3, 1, 0));

        var always = new LosingTheRace(losses: 5);
        await using var other = new ServiceCollection().AddSingleton<ITenantStarterRoles>(always).BuildServiceProvider();

        var given = await TenantStarterRolesRun.RunAsync(other.GetRequiredService<IServiceScopeFactory>(), Cancellation);

        always.Runs.Should().Be(3);
        given.Failed.Should().ContainSingle().Which.Should().BeOfType<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Cancelling_ends_the_run()
    {
        await using var services = new ServiceCollection().AddSingleton<ITenantStarterRoles>(new Failing(new InvalidOperationException())).BuildServiceProvider();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await FluentActions.Awaiting(() => TenantStarterRolesRun.RunAsync(services.GetRequiredService<IServiceScopeFactory>(), cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>One for each scope, to tell two scopes apart.</summary>
    private sealed class ScopeMark;

    private sealed class Counting(IServiceProvider scope, TenantStarterRolesFollowed answer) : ITenantStarterRoles
    {
        public Task<TenantStarterRolesFollowed> FollowAsync(CancellationToken cancellationToken)
        {
            scope.GetRequiredService<List<ScopeMark>>().Add(scope.GetRequiredService<ScopeMark>());
            return Task.FromResult(answer);
        }
    }

    private sealed class Failing(Exception failure) : ITenantStarterRoles
    {
        public Task<TenantStarterRolesFollowed> FollowAsync(CancellationToken cancellationToken) => Task.FromException<TenantStarterRolesFollowed>(failure);
    }

    private sealed class LosingTheRace(int losses) : ITenantStarterRoles
    {
        public int Runs { get; private set; }

        public Task<TenantStarterRolesFollowed> FollowAsync(CancellationToken cancellationToken)
            => ++Runs <= losses
                ? throw new ConcurrencyConflictException("another change committed first")
                : Task.FromResult(new TenantStarterRolesFollowed(0, 1, 0));
    }
}
