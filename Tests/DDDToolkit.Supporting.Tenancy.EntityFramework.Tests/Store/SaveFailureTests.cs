using DDDToolkit.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// A save of Tenancy's store that fails is offered to the registered translators, in order: the first refusal one
/// makes of it is what the caller gets, and a failure none knows leaves exactly as it was thrown. A save that was
/// refused already, by a unique index among others, is not theirs to translate.
/// </summary>
public sealed class SaveFailureTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_translator_turns_a_failed_save_into_its_refusal_and_anything_else_is_rethrown()
    {
        var first = new Recording();
        using var services = new TestServices(configure: collection => collection
            .AddSingleton<ITenancySaveFailures>(first)
            .AddSingleton<ITenancySaveFailures, KeptAtCommit>()
            .AddSingleton<ITenancySaveFailures, RefusesEverything>());

        // A rule the database keeps and no index states, such as one a trigger checks when the save commits.
        var commit = new CommitRefused();
        await using (var scope = services.Scope())
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            services.Hook.BeforeSave(scope.ServiceProvider.Tenancy(), () => throw commit);

            var refusal = await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => scope.ServiceProvider.Tenants().ProvisionAsync(Harbor(), Cancellation));
            refusal.InnerException.Should().BeSameAs(commit, "the refusal keeps the failure it stands for, so a log of it names the rule");
        }

        services.Hook.Fired.Should().BeTrue();
        first.Offered.Should().ContainSingle().Which.Should().BeSameAs(commit, "every translator is offered the failure, in order");

        // A failure no translator knows is thrown on as it was.
        var failure = new InvalidOperationException("The disk is full.");
        await using (var scope = services.Scope())
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            services.Hook.BeforeSave(scope.ServiceProvider.Tenancy(), () => throw failure);

            var thrown = await FluentActions.Awaiting(() => scope.ServiceProvider.Tenants().ProvisionAsync(Harbor("orchard"), Cancellation))
                .Should().ThrowAsync<InvalidOperationException>();
            thrown.Which.Should().BeSameAs(failure);
        }

        first.Offered.Should().HaveCount(2).And.EndWith(failure);
    }

    [Fact]
    public async Task A_save_an_index_refused_is_not_offered_to_a_translator()
    {
        var recording = new Recording();
        using var services = new TestServices(configure: collection => collection
            .AddSingleton<ITenancySaveFailures>(recording)
            .AddSingleton<ITenancySaveFailures, RefusesEverything>());

        // Harbor is provisioned by another command while this one saves: the check that the slug is free passed,
        // and the unique index refuses the save, with the refusal it declares.
        await using (var scope = services.Scope())
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            services.Hook.BeforeSave(scope.ServiceProvider.Tenancy(), () => services.ProvisionAsync("harbor"));

            var refusal = await Refused.WithCodeAsync(TenancyRefusals.SlugTaken, () => scope.ServiceProvider.Tenants().ProvisionAsync(Harbor(), Cancellation));
            refusal.Arguments.Should().Contain("Slug", "harbor");
            refusal.InnerException.Should().BeOfType<DbUpdateException>("the refusal keeps the failure it stands for, so a log of it names the index");
        }

        services.Hook.Fired.Should().BeTrue();
        recording.Offered.Should().BeEmpty("a refusal is an answer already, and the one that refuses everything never saw it");
    }

    [Fact]
    public async Task A_translator_that_throws_is_a_fault_of_its_own_and_keeps_the_failure()
    {
        using var services = new TestServices(configure: collection => collection
            .AddSingleton<ITenancySaveFailures, Broken>()
            .AddSingleton<ITenancySaveFailures, KeptAtCommit>());
        await services.ProvisionAsync("harbor");

        var failure = new InvalidOperationException("The disk is full.");
        await using var scope = services.Scope();
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            services.Hook.BeforeSave(scope.ServiceProvider.Tenancy(), () => throw failure);

            var thrown = await FluentActions.Awaiting(() => scope.ServiceProvider.Tenants().ProvisionAsync(Harbor("orchard"), Cancellation))
                .Should().ThrowAsync<AggregateException>("a translator that throws is not one that knows nothing of the failure");
            thrown.WithMessage($"{nameof(Broken)} threw when it was asked whether a failed save*");
            thrown.Which.InnerExceptions.Should().HaveCount(2);
            thrown.Which.InnerExceptions[0].Should().BeOfType<FormatException>();
            thrown.Which.InnerExceptions[1].Should().BeSameAs(failure);
        }
    }

    private static HostTenancy.TenantToProvision Harbor(string slug = "harbor")
        => new(slug, "Harbor Works", TenantShape.Hierarchical, "Harbor Works", "company", Guid.NewGuid(), "Ada");

    /// <summary>Knows nothing, and keeps what it was offered.</summary>
    private sealed class Recording : ITenancySaveFailures
    {
        private readonly List<Exception> _offered = [];

        public IReadOnlyList<Exception> Offered => _offered;

        public RefusalException? Translate(Exception failure, DbContext context)
        {
            context.Should().BeOfType<TestTenancyContext>();
            _offered.Add(failure);
            return null;
        }
    }

    /// <summary>What a database says when a rule it checks at commit does not hold.</summary>
    private sealed class CommitRefused() : Exception("The commit was refused: no administrator would remain.");

    /// <summary>Knows the one rule that is kept at commit.</summary>
    private sealed class KeptAtCommit : ITenancySaveFailures
    {
        public RefusalException? Translate(Exception failure, DbContext context)
            => failure is CommitRefused ? TenancyRefusals.Of(TenancyRefusals.LastAdmin) : null;
    }

    /// <summary>A translator with a fault of its own.</summary>
    private sealed class Broken : ITenancySaveFailures
    {
        public RefusalException? Translate(Exception failure, DbContext context) => throw new FormatException("A code of no known shape.");
    }

    /// <summary>Would refuse anything, and is registered after the one that knows the failure, so it is never asked for it.</summary>
    private sealed class RefusesEverything : ITenancySaveFailures
    {
        public RefusalException? Translate(Exception failure, DbContext context)
            => failure is DbUpdateException or RefusalException or CommitRefused ? TenancyRefusals.Of(TenancyRefusals.OtherTenant) : null;
    }
}
