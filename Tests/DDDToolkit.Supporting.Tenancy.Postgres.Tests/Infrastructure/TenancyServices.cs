using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// An application's services over one Postgres database, as a host that keeps Tenancy on Postgres registers them:
/// the toolkit's Entity Framework integration with an outbox on Tenancy's context that keeps the events that change
/// access in the access history, Tenancy through the TestHost,
/// row level security, and <c>AddTenancyPostgres</c>, which requires explicit callers and leaves the rights to the
/// database. Both contexts connect as the login role and run every query as the caller the flow of work began, and
/// both are named as the database's tables were made (<see cref="TestDatabase.Names"/>). The application's clock
/// runs with the database's (<see cref="DatabaseClock"/>) unless a test registers another.
/// </summary>
public sealed class TenancyServices : IAsyncDisposable
{
    /// <param name="database">The database, which the contexts connect to as the login role, under its naming.</param>
    /// <param name="configure">Registers what a test needs before Tenancy is registered, such as a clock.</param>
    /// <param name="rowLevelSecurity">Whether the contexts set the caller on the connection; without it they run as the login role, which owns the tables.</param>
    /// <param name="roles">Changes the roles row level security switches to.</param>
    /// <param name="catalogue">The application's catalogue, or the TestHost's.</param>
    /// <param name="contexts">Adds to the options of both contexts, after everything else, such as an interceptor that records what they send.</param>
    /// <param name="databaseKeepsRights">
    /// Whether the rights are the database's to write and to answer about, as <c>AddTenancyPostgres</c> says they are.
    /// False for a database the access files were never applied to, which has no trigger to write them, and for a
    /// question worked out in C# to compare the database's answer with: the save then writes the rights, and the
    /// questions read them, as on any other database.
    /// </param>
    /// <param name="pooled">
    /// Whether the contexts come from a pool, as a host registers them that runs the parts of one request side by
    /// side, each on a context of its own: a factory hands a context to each, and the request's own is taken from the
    /// same pool (<c>AddScopedFromPool</c>), bound to the request's scope and given back with it. A pool builds a
    /// context's options once, from the application's services rather than a request's, and hands one instance to one
    /// caller after another. The options are the other mode's in every respect, the toolkit's and Tenancy's save
    /// interceptors included, so a pooled context saves as any other does: its events go to the outbox and its rows
    /// are checked against the tenant of whoever rented it.
    /// </param>
    /// <param name="operators">The token roles of the host's operators, as <c>TenancyOptions.OperatorTokenRoles</c> lists them.</param>
    /// <param name="more">
    /// Registers further contexts of modules the host has, each with the options the two here get: handed the
    /// services and what configures a context's options.
    /// </param>
    public TenancyServices(
        TestDatabase database,
        Action<IServiceCollection>? configure = null,
        bool rowLevelSecurity = true,
        Action<PostgresRowLevelSecurityOptions>? roles = null,
        ApplicationCatalogue? catalogue = null,
        Action<DbContextOptionsBuilder>? contexts = null,
        bool databaseKeepsRights = true,
        bool pooled = false,
        IReadOnlyCollection<string>? operators = null,
        Action<IServiceCollection, Action<IServiceProvider, DbContextOptionsBuilder>>? more = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);

        // Unless the test brought a clock of its own: one that runs with the database's, as the policies expect.
        // Made when something first asks the time, so services that never reach a database measure none.
        services.TryAddSingleton<TimeProvider>(_ => DatabaseClock.Of(database.ConnectionString));

        // Every event of Tenancy's that changes access is kept in its access history as well, so every use case a
        // test runs that changes it adds to the history under the history's own policies.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<TestTenancyContext>(outbox => outbox
            .AddTenancyDomainEvents<TenantId, SeatId, OrganizationUnitId, RoleId>()
            .AddTenancyInvitationEvents<TenantId, InvitationId, OrganizationUnitId, RoleId, SeatId>()
            .KeepEventLog(log => log.AddTenancyEventLog<TenantId, SeatId, OrganizationUnitId, RoleId>())));
        TestHostTenancy.Add(services, options =>
        {
            options.Catalogue = catalogue ?? HostCatalogue.Application;
            foreach (var tokenRole in operators ?? [])
            {
                options.OperatorTokenRoles.Add(tokenRole);
            }
        });
        services.AddPostgresRowLevelSecurity(roles);
        services.AddTenancyPostgres();
        if (!databaseKeepsRights)
        {
            services.Configure<TenancyStoreOptions>(options => options.DatabaseKeepsRights = false);
        }

        // The naming is part of each context's options, next to the provider: a pool builds them once, as a request does.
        void Options(DbContextOptionsBuilder options, IServiceProvider provider)
            => Configure(database.Names.Configure(options.UseNpgsql(database.ConnectionString)), provider, rowLevelSecurity, contexts);

        more?.Invoke(services, (provider, options) => Options(options, provider));

        if (pooled)
        {
            // The same options as the other mode, built once with the application's services. The request's own
            // context is one of the pool's, rented when its scope first asks for it and bound to that scope.
            services.AddPooledDbContextFactory<TestTenancyContext>((provider, options) => Options(options, provider));
            services.AddPooledDbContextFactory<WidgetContext>((provider, options) => Options(options, provider));
            services.AddScopedFromPool<TestTenancyContext>();
            services.AddScopedFromPool<WidgetContext>();
        }
        else
        {
            services.AddDbContext<TestTenancyContext>((provider, options) => Options(options, provider));
            services.AddDbContext<WidgetContext>((provider, options) => Options(options, provider));
        }

        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public ServiceProvider Provider { get; }

    public AsyncServiceScope Scope() => Provider.CreateAsyncScope();

    /// <summary>Runs <paramref name="act"/> in a scope of its own, as whoever the caller begun around it is.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> act)
    {
        await using var scope = Scope();
        return await act(scope.ServiceProvider);
    }

    /// <summary>Runs <paramref name="act"/> in a scope of its own, as whoever the caller begun around it is.</summary>
    public Task InScopeAsync(Func<IServiceProvider, Task> act)
        => InScopeAsync(async services =>
        {
            await act(services);
            return true;
        });

    /// <summary>Runs <paramref name="act"/> as system work in <paramref name="tenant"/>, in Tenancy's scope unless another is given.</summary>
    public async Task<T> BySystemIn<T>(TenantId tenant, Func<IServiceProvider, Task<T>> act, string scope = TenancyWork.SystemScope)
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant, scope: scope))
        {
            return await InScopeAsync(act);
        }
    }

    /// <summary>Runs <paramref name="act"/> as system work in <paramref name="tenant"/>, in Tenancy's scope unless another is given.</summary>
    public Task BySystemIn(TenantId tenant, Func<IServiceProvider, Task> act, string scope = TenancyWork.SystemScope)
        => BySystemIn(tenant, async services =>
        {
            await act(services);
            return true;
        }, scope);

    /// <summary>
    /// Runs <paramref name="act"/> as a person signed in with <paramref name="identity"/>, through their seat in
    /// <paramref name="tenant"/>: the toolkit's caller is the user, whose role and claims reach Postgres, and the
    /// Tenancy caller the seat, whose tenant does.
    /// </summary>
    public async Task<T> BySeat<T>(Guid identity, TenantId tenant, SeatId seat, Func<IServiceProvider, Task<T>> act)
    {
        using (Callers.Begin(Caller.User(identity)))
        using (TenancyCallers.Begin(HostCaller.InSeat(tenant, seat)))
        {
            return await InScopeAsync(act);
        }
    }

    /// <summary>Runs <paramref name="act"/> as a person through their seat in <paramref name="tenant"/>.</summary>
    public Task BySeat(Guid identity, TenantId tenant, SeatId seat, Func<IServiceProvider, Task> act)
        => BySeat(identity, tenant, seat, async services =>
        {
            await act(services);
            return true;
        });

    /// <summary>
    /// Runs <paramref name="act"/> as a person signed in with <paramref name="identity"/> who names no tenant: the
    /// toolkit's caller is the user, and there is no Tenancy caller, as in a request that accepts an invitation.
    /// </summary>
    public async Task<T> BySignedInUser<T>(Guid identity, Func<IServiceProvider, Task<T>> act)
    {
        using (Callers.Begin(Caller.User(identity)))
        using (TenancyCallers.BeginNone())
        {
            return await InScopeAsync(act);
        }
    }

    public ValueTask DisposeAsync() => Provider.DisposeAsync();

    private static void Configure(DbContextOptionsBuilder options, IServiceProvider provider, bool rowLevelSecurity, Action<DbContextOptionsBuilder>? contexts)
    {
        options.UseDDDToolkit(provider).UseTenancy(provider);
        if (rowLevelSecurity)
        {
            options.UsePostgresRowLevelSecurity(provider);
        }

        contexts?.Invoke(options);
    }
}

/// <summary>What a test resolves from a scope, by name.</summary>
public static class ScopedServices
{
    public static HostTenancy.TenantCommands Tenants(this IServiceProvider services) => services.GetRequiredService<HostTenancy.TenantCommands>();

    public static HostTenancy.OrganizationCommands Organization(this IServiceProvider services) => services.GetRequiredService<HostTenancy.OrganizationCommands>();

    public static HostTenancy.SeatCommands Seats(this IServiceProvider services) => services.GetRequiredService<HostTenancy.SeatCommands>();

    public static HostTenancy.RoleCommands Roles(this IServiceProvider services) => services.GetRequiredService<HostTenancy.RoleCommands>();

    public static TestTenancyContext Tenancy(this IServiceProvider services) => services.GetRequiredService<TestTenancyContext>();

    public static WidgetContext Widgets(this IServiceProvider services) => services.GetRequiredService<WidgetContext>();

    public static HostTenancy.TenancyDirectory Directory(this IServiceProvider services) => services.GetRequiredService<HostTenancy.TenancyDirectory>();

    public static HostTenancy.TenantDirectory TenantDirectory(this IServiceProvider services) => services.GetRequiredService<HostTenancy.TenantDirectory>();

    public static HostTenancy.InvitationCommands<DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostInvitation, InvitationId> Invitations(this IServiceProvider services)
        => services.GetRequiredService<HostTenancy.InvitationCommands<DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostInvitation, InvitationId>>();

    public static TallyContext Tallies(this IServiceProvider services) => services.GetRequiredService<TallyContext>();

    public static ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId> Answers(this IServiceProvider services)
        => services.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>();
}
