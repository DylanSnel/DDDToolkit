using System.Data.Common;
using DDDToolkit.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Examples.Hosting;

/// <summary>
/// A host's connections to its Postgres, as two data sources over one connection string: one for what answers
/// requests and one for what runs in the background, each bounded by the <see cref="PostgresPoolBudget"/>. The
/// host makes one for the one role it logs in as and hands it to its modules (<see cref="ModuleHost.OnPostgres"/>);
/// the first module that registers a context on it makes it the container's, which closes it when the host stops.
/// </summary>
/// <remarks>
/// A module registers its context on it with <see cref="AddContext{TContext}"/>, which gives that
/// context a pool per data source and picks between them at each rental. No module makes a data source of its
/// own, so the two maxima here are the whole of what the host holds.
/// </remarks>
public sealed class PostgresPools : IAsyncDisposable
{
    private PostgresPools(NpgsqlDataSource requests, NpgsqlDataSource background)
    {
        Requests = requests;
        Background = background;
    }

    /// <summary>The connections requests are answered on, and everything else that says who it runs as.</summary>
    public NpgsqlDataSource Requests { get; }

    /// <summary>The connections of the toolkit's own bookkeeping: the outbox pollers, retention, the start-up checks.</summary>
    public NpgsqlDataSource Background { get; }

    /// <summary>
    /// Two data sources over <paramref name="connectionString"/>, named <c>{name}-requests</c> and
    /// <c>{name}-background</c> to the database, each with the budget's maximum, no connection kept open for
    /// nobody, and its lifetimes and timeout. Whatever the connection string says of those is replaced.
    /// </summary>
    public static PostgresPools Create(string connectionString, PostgresPoolBudget budget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(budget);

        return new PostgresPools(
            NpgsqlDataSource.Create(Budgeted(connectionString, budget, budget.Requests, "requests")),
            NpgsqlDataSource.Create(Budgeted(connectionString, budget, budget.Background, "background")));
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> on these connections: a context pool per data source, and one
    /// factory over both that picks at each rental. A context rented while the toolkit's own system caller is
    /// current, which is its bookkeeping, comes from the background pool; every other from the requests pool. The
    /// scope's own context is taken from the same factory (<see cref="PooledContexts.AddScopedFromPool{TContext}"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scope has one context, and a context runs one query at a time; reads that run side by side within one
    /// scope, as the resolvers of one GraphQL request do, would otherwise meet on it. So a read takes a context
    /// from the <see cref="IDbContextFactory{TContext}"/> for its one query and gives it back, <c>await using</c>.
    /// What changes data keeps the scope's context, the unit of work, which is rented when the scope first asks
    /// for it and given back when the scope ends.
    /// </para>
    /// <para>
    /// The purpose cannot be chosen inside an options callback: a pool builds its options once, so every context
    /// of it would use whichever data source the first rental asked for. Hence two pools, and a choice per rental.
    /// </para>
    /// <para>
    /// The provider is this method's to set: Npgsql on the pool's data source, the migrations from the context's own
    /// assembly. <paramref name="wire"/> adds the rest, in the order the module writes it: <c>UseDDDToolkit</c> among
    /// it, which keeps the migration history in the module's schema, where the design-time factory's
    /// <c>UseDDDToolkitDesignTime()</c> keeps it too. It runs once per pool, with the application's own services, so it
    /// reads no caller, no request and nothing scoped; what <c>services.ConfigureDbContext</c> added for the
    /// context is applied after it, as a pool registered the usual way applies it.
    /// </para>
    /// <para>
    /// A pool builds its options once, and every context of it shares them: the same filters and the same
    /// interceptors, each of which asks who is calling when it is used. A context that goes back to a pool tracks
    /// nothing and holds no connection, and whoever rents it next is its own caller in its own tenant. A pool's
    /// size bounds how many idle contexts are kept, not how many connections are open: that is the data source's.
    /// </para>
    /// <para>
    /// The migrations are applied by whoever owns the database, from the files the export writes, and the host checks
    /// that none is missing with the one call it makes for every context marked <c>[SupabaseMigrations]</c>,
    /// <c>AddSupabaseMigrations()</c>, which the Supabase package writes into it. That check asks the running context,
    /// whose migrations are the ones the design-time factory the build writes beside the context finds: both read the
    /// context's assembly. Nothing here registers it, so a module says nothing of Supabase when it registers its context.
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">The module's context.</typeparam>
    /// <param name="services">The host's services.</param>
    /// <param name="wire">
    /// What the context is wired with after its provider: <c>UseDDDToolkit</c>, which brings what the host's
    /// registrations bring, row level security and Tenancy's save check among them, and whatever else the module's
    /// contexts need.
    /// </param>
    public IServiceCollection AddContext<TContext>(IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> wire)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(wire);

        DbContextOptions<TContext> Options(IServiceProvider application, DbDataSource source)
        {
            var options = new DbContextOptionsBuilder<TContext>();
            options.UseApplicationServiceProvider(application);
            options.UseNpgsql(source);
            wire(application, options);
            foreach (var added in application.GetServices<IDbContextOptionsConfiguration<TContext>>())
            {
                added.Configure(application, options);
            }

            return options.Options;
        }

        // The host's container hands these pools out, so it is also the one that disposes them, with the host:
        // registered by a factory, and asked for when the first context pool is made.
        services.TryAddSingleton(_ => this);

        services.AddSingleton(application =>
        {
            var pools = application.GetRequiredService<PostgresPools>();
            return new ContextsByPurpose<TContext>(Options(application, pools.Requests), Options(application, pools.Background));
        });
        services.AddSingleton<IDbContextFactory<TContext>>(application => application.GetRequiredService<ContextsByPurpose<TContext>>());

        // The start-up checks find the host's contexts by their options, so the options are registered as a pool
        // registered the usual way registers them: those every request's context has.
        services.AddSingleton(application => application.GetRequiredService<ContextsByPurpose<TContext>>().RequestOptions);
        services.AddSingleton<DbContextOptions>(application => application.GetRequiredService<DbContextOptions<TContext>>());

        return services.AddScopedFromPool<TContext>();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Requests.DisposeAsync().ConfigureAwait(false);
        await Background.DisposeAsync().ConfigureAwait(false);
    }

    private static string Budgeted(string connectionString, PostgresPoolBudget budget, int maximum, string purpose)
        => new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = maximum,
            ConnectionIdleLifetime = (int)budget.IdleLifetime.TotalSeconds,
            ConnectionLifetime = (int)budget.Lifetime.TotalSeconds,
            Timeout = (int)budget.Timeout.TotalSeconds,
            ApplicationName = $"{budget.ApplicationName}-{purpose}",
        }.ConnectionString;
}
