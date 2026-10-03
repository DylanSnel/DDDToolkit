using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The checks a host runs at start-up: a context built without the toolkit's interceptors saves, and silently
/// checks nothing, and a context without the row level security interceptor runs every command as the login
/// role. Both are found before the first request instead of by whoever notices what never happened.
/// </summary>
public sealed class EntityFrameworkChecksTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    /// <summary>A host whose apiary context gets <paramref name="options"/>, with an outbox as <paramref name="toolkit"/> says.</summary>
    private ServiceProvider Host<TContext>(Action<IServiceProvider, DbContextOptionsBuilder> options, Action<DDDEntityFrameworkOptions>? toolkit = null)
        where TContext : ApiaryContext
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework(toolkit);
        services.AddPostgresRowLevelSecurity();
        services.AddDbContext<ApiaryContext, TContext>((provider, builder) =>
        {
            builder.UseSqlite(_db.Connection);
            options(provider, builder);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void Check(ServiceProvider host, Action<DbContext> check)
    {
        using var scope = host.CreateScope();
        check(scope.ServiceProvider.GetRequiredService<ApiaryContext>());
    }

    private static readonly Action<DDDEntityFrameworkOptions> OutboxOfItsOwn = options => options.UseOutbox<ApiaryContext>();

    [Fact]
    public void A_context_without_the_toolkit_fails_the_wiring_check()
    {
        using var bare = Host<ApiaryContext>((_, _) => { }, OutboxOfItsOwn);

        var check = () => Check(bare, EntityFrameworkChecks.EnsureToolkitWired);

        check.Should().Throw<InvalidOperationException>().WithMessage(
            "'ApiaryContext' was built without the DDDToolkit interceptors: " +
            "it has no PublishDomainEventsInterceptor (its domain events would stay on their aggregates, delivered to nobody and stored nowhere), " +
            "no InvariantInterceptor (its aggregates' invariants would not be checked), " +
            "no AggregateVersionInterceptor (its aggregates' versions would not be bumped, so two saves of one aggregate would not conflict). " +
            "Configure it with options.UseDDDToolkit(serviceProvider), in the options callback of AddDbContext or of a context pool.");

        // Added by hand with one left out, the check names the one that is missing.
        using var partial = Host<ApiaryContext>(
            (provider, options) => options.AddInterceptors(
                new PublishDomainEventsInterceptor(provider, provider.GetRequiredService<DDDEntityFrameworkOptions>()),
                provider.GetRequiredService<AggregateVersionInterceptor>()),
            OutboxOfItsOwn);
        FluentActions.Invoking(() => Check(partial, EntityFrameworkChecks.EnsureToolkitWired)).Should().Throw<InvalidOperationException>()
            .WithMessage("'ApiaryContext' was built without the DDDToolkit interceptors: it has no InvariantInterceptor (its aggregates' invariants would not be checked). Configure it*");

        // In another order than UseDDDToolkit adds them, an invariant would be checked before a handler changed what it is about.
        using var shuffled = Host<ApiaryContext>(
            (provider, options) => options.AddInterceptors(
                provider.GetRequiredService<InvariantInterceptor>(),
                new PublishDomainEventsInterceptor(provider, provider.GetRequiredService<DDDEntityFrameworkOptions>()),
                provider.GetRequiredService<AggregateVersionInterceptor>()),
            OutboxOfItsOwn);
        FluentActions.Invoking(() => Check(shuffled, EntityFrameworkChecks.EnsureToolkitWired)).Should().Throw<InvalidOperationException>()
            .WithMessage("'ApiaryContext' has the DDDToolkit interceptors in another order than UseDDDToolkit adds them*domain events, then invariants, then versions.");

        FluentActions.Invoking(() => EntityFrameworkChecks.EnsureToolkitWired(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_context_with_the_toolkit_passes_it()
    {
        // With an outbox of its own, with the one every context shares, with none, and with a log that is kept.
        using var own = Host<ApiaryContext>((provider, options) => options.UseDDDToolkit(provider), OutboxOfItsOwn);
        using var shared = Host<ApiaryContext>((provider, options) => options.UseDDDToolkit(provider), options => options.UseOutbox());
        using var inProcess = Host<ApiaryContext>((provider, options) => options.UseDDDToolkit(provider));
        using var logged = Host<LoggedApiaryContext>((provider, options) => options.UseDDDToolkit(provider), options => options.UseOutbox<ApiaryContext>(outbox => outbox.KeepEventLog()));

        foreach (var host in (ServiceProvider[])[own, shared, inProcess, logged])
        {
            FluentActions.Invoking(() => Check(host, EntityFrameworkChecks.EnsureToolkitWired)).Should().NotThrow();
        }

        // The three that decide what is saved, added by hand in their order, pass as well.
        using var byHand = Host<ApiaryContext>(
            (provider, options) => options.AddInterceptors(
                new PublishDomainEventsInterceptor(provider, provider.GetRequiredService<DDDEntityFrameworkOptions>()),
                provider.GetRequiredService<InvariantInterceptor>(),
                provider.GetRequiredService<AggregateVersionInterceptor>()),
            OutboxOfItsOwn);
        FluentActions.Invoking(() => Check(byHand, EntityFrameworkChecks.EnsureToolkitWired)).Should().NotThrow();
    }

    [Fact]
    public void A_log_nobody_keeps_fails_the_wiring_check()
    {
        using var unkept = Host<LoggedApiaryContext>((provider, options) => options.UseDDDToolkit(provider), OutboxOfItsOwn);

        FluentActions.Invoking(() => Check(unkept, EntityFrameworkChecks.EnsureToolkitWired)).Should().Throw<InvalidOperationException>().WithMessage(
            "The model of 'LoggedApiaryContext' maps an event log, but nothing keeps events in it, so the table would stay empty. " +
            "Call outbox.KeepEventLog() in UseOutbox<LoggedApiaryContext>(...), or take modelBuilder.AddEventLog(...) out.");

        // Without any outbox the events are dispatched in process, and a log has nothing to write it either.
        using var inProcess = Host<LoggedApiaryContext>((provider, options) => options.UseDDDToolkit(provider));
        FluentActions.Invoking(() => Check(inProcess, EntityFrameworkChecks.EnsureToolkitWired)).Should().Throw<InvalidOperationException>().WithMessage("The model of 'LoggedApiaryContext' maps an event log, but nothing keeps events in it*");

        // The other way round, for an outbox of the context's own: it keeps a log, or has a table, the model does not map.
        using var unmapped = Host<ApiaryContext>((provider, options) => options.UseDDDToolkit(provider), options => options.UseOutbox<ApiaryContext>(outbox => outbox.KeepEventLog()));
        FluentActions.Invoking(() => Check(unmapped, EntityFrameworkChecks.EnsureToolkitWired)).Should().Throw<InvalidOperationException>().WithMessage(
            "The outbox of 'ApiaryContext' keeps an event log but the context's model does not contain the event log table, so its first save that raises a kept event would fail.*");

        using var noTable = new ServiceCollection()
            .AddDDDToolkitEntityFramework(options => options.UseOutbox<DeskContext>())
            .AddDbContext<DeskContext>((provider, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = noTable.CreateScope();
        FluentActions.Invoking(() => EntityFrameworkChecks.EnsureToolkitWired(scope.ServiceProvider.GetRequiredService<DeskContext>())).Should().Throw<InvalidOperationException>().WithMessage(
            "An outbox is configured for 'DeskContext' but its model does not contain the outbox table, so its first save that raises an event would fail.*");

        // The outbox every context shares says nothing about one of them: a context that raises no event needs none of its tables.
        using var sharing = new ServiceCollection()
            .AddDDDToolkitEntityFramework(options => options.UseOutbox(outbox => outbox.KeepEventLog()))
            .AddDbContext<DeskContext>((provider, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var sharingScope = sharing.CreateScope();
        FluentActions.Invoking(() => EntityFrameworkChecks.EnsureToolkitWired(sharingScope.ServiceProvider.GetRequiredService<DeskContext>())).Should().NotThrow();
    }

    [Fact]
    public void Every_registered_context_is_found()
    {
        // The four ways Entity Framework registers a context, a context registered twice, and one behind a service type.
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddDbContext<DeskContext>(options => options.UseSqlite(_db.Connection), optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<LibraryContext>(options => options.UseSqlite(_db.Connection));
        services.AddPooledDbContextFactory<YardContext>(options => options.UseSqlite(_db.Connection));
        services.AddDbContextPool<AnnalsContext>(options => options.UseSqlite(_db.Connection));
        services.AddDbContext<ApiaryContext, LoggedApiaryContext>(options => options.UseSqlite(_db.Connection));
        services.AddDbContextFactory<DeskContext>(options => options.UseSqlite(_db.Connection));
        using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = host.CreateScope();

        EntityFrameworkChecks.RegisteredContexts(scope.ServiceProvider).Should().Equal(
            [typeof(AnnalsContext), typeof(DeskContext), typeof(LibraryContext), typeof(LoggedApiaryContext), typeof(YardContext)],
            "each context once, by its full name, however it was registered");

        using var none = new ServiceCollection().BuildServiceProvider();
        EntityFrameworkChecks.RegisteredContexts(none).Should().BeEmpty();
        FluentActions.Invoking(() => EntityFrameworkChecks.RegisteredContexts(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_context_without_the_interceptor_fails_the_row_level_security_check()
    {
        using var without = Host<ApiaryContext>((provider, options) => options.UseDDDToolkit(provider));
        using var with = Host<ApiaryContext>((provider, options) => options.UseDDDToolkit(provider).UsePostgresRowLevelSecurity(provider));

        FluentActions.Invoking(() => Check(without, PostgresRowAccessChecks.EnsureRowLevelSecurityWired)).Should().Throw<InvalidOperationException>().WithMessage(
            "'ApiaryContext' does not run its commands as the caller: its options have no PostgresRowLevelSecurityInterceptor, so every command would run as the role the application logged in as. " +
            "Configure it with options.UsePostgresRowLevelSecurity(serviceProvider), or UseSupabaseRowLevelSecurity on Supabase, after services.AddPostgresRowLevelSecurity().");
        FluentActions.Invoking(() => Check(with, PostgresRowAccessChecks.EnsureRowLevelSecurityWired)).Should().NotThrow();
        FluentActions.Invoking(() => PostgresRowAccessChecks.EnsureRowLevelSecurityWired(null!)).Should().Throw<ArgumentNullException>();
    }
}
