using DDDToolkit.EntityFramework;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>How the contexts of a <see cref="TestServices"/> add Tenancy's interceptor.</summary>
public enum Wiring
{
    /// <summary><c>UseDDDToolkit</c> alone, as documented: it adds Tenancy's interceptor, which <c>AddTenancy</c> brought.</summary>
    Wired,

    /// <summary><c>UseDDDToolkitCore</c> only: Tenancy's interceptor left out.</summary>
    WithoutTenancy,

    /// <summary><c>UseTenancy</c> and then <c>UseDDDToolkitCore</c>: the wrong order.</summary>
    TenancyFirst,

    /// <summary>The chain written out before the one call, <c>UseDDDToolkit</c> and then <c>UseTenancy</c>.</summary>
    WrittenOut,

    /// <summary><c>UseDDDToolkitCore</c> and then <c>UseTenancy</c>: the parts taken one by one.</summary>
    OneByOne,
}

/// <summary>
/// An application's services over one database, an in-memory SQLite one unless a test gives it another: the
/// toolkit's Entity Framework integration with an outbox on Tenancy's context that keeps the events that change
/// access in the access history, Tenancy registered through the TestHost, and both contexts on that database, Tenancy's and the widgets'. The tables are created on
/// construction, unless the database has them already: a Postgres copy of a template, or one borrowed from
/// services that already created them, two applications over one database, as after a deployment.
/// <para>
/// The helpers run each command as one caller in a scope of its own, the way a request does.
/// </para>
/// <para>
/// With <c>inFile</c> the database is a file, and every context opens a connection of its own to it, as every
/// context does on Postgres.
/// </para>
/// <para>
/// With <c>pooled</c> the contexts come from a pool, as a host registers them whose reads each take a context of
/// their own: the options are the same in every respect, built once with the application's services, and the scope's
/// own context is one of the pool's, bound to that scope and given back with it.
/// </para>
/// </summary>
public sealed class TestServices : IDisposable
{
    private readonly bool _ownsDatabase;

    /// <param name="wiring">How the contexts add Tenancy's interceptor.</param>
    /// <param name="configure">Registers what a test needs before Tenancy is registered, such as a clock.</param>
    /// <param name="database">The database, or a new in-memory SQLite one.</param>
    /// <param name="inFile">A new SQLite database in a file rather than in memory, when no database is given.</param>
    /// <param name="ownsDatabase">Whether these services dispose of the <paramref name="database"/> they were given.</param>
    /// <param name="pooled">
    /// Whether the contexts come from a pool (<c>AddPooledDbContextFactory</c> with <c>AddScopedFromPool</c>) rather
    /// than being made for each scope (<c>AddDbContext</c>).
    /// </param>
    /// <param name="afterTenancy">Registers what a host registers too late: after Tenancy is registered.</param>
    public TestServices(
        Wiring wiring = Wiring.Wired,
        Action<IServiceCollection>? configure = null,
        TestDatabase? database = null,
        bool inFile = false,
        bool ownsDatabase = false,
        bool pooled = false,
        Action<IServiceCollection>? afterTenancy = null)
    {
        _ownsDatabase = database is null || ownsDatabase;
        Database = database ?? (inFile ? SqliteDatabase.InFile() : new SqliteDatabase());

        var services = new ServiceCollection();
        services.AddSingleton(Commands);

        // Before Tenancy, so a clock a test registers is the one Tenancy keeps.
        configure?.Invoke(services);

        // Every event of Tenancy's that changes access is kept in its access history as well, as an application that
        // maps the log keeps them.
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<TestTenancyContext>(outbox => outbox
            .AddTenancyDomainEvents<TenantId, SeatId, OrganizationUnitId, RoleId>()
            .AddTenancyInvitationEvents<TenantId, InvitationId, OrganizationUnitId, RoleId, SeatId>()
            .KeepEventLog(log => log.AddTenancyEventLog<TenantId, SeatId, OrganizationUnitId, RoleId>())));
        TestHostTenancy.Add(services);
        afterTenancy?.Invoke(services);
        if (pooled)
        {
            // A pool builds the options once, with the application's services, and every context of it shares them.
            services.AddPooledDbContextFactory<TestTenancyContext>((provider, options) => Configure(options, provider, wiring));
            services.AddPooledDbContextFactory<TestWidgetContext>((provider, options) => Configure(options, provider, wiring));
            services.AddScopedFromPool<TestTenancyContext>();
            services.AddScopedFromPool<TestWidgetContext>();
        }
        else
        {
            services.AddDbContext<TestTenancyContext>((provider, options) => Configure(options, provider, wiring));
            services.AddDbContext<TestWidgetContext>((provider, options) => Configure(options, provider, wiring));
        }

        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        if (!Database.HasTables)
        {
            Database.HasTables = true;
            using var scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<TestTenancyContext>().Database.EnsureCreated();

            // EnsureCreated does nothing on a database that already has tables, so the widgets' own are created
            // by hand; its views over Tenancy's tables are not tables, and are not created.
            scope.ServiceProvider.GetRequiredService<TestWidgetContext>().GetService<IRelationalDatabaseCreator>().CreateTables();
        }
    }

    public TestDatabase Database { get; }

    /// <summary>The database, for a test that runs on SQLite alone.</summary>
    public SqliteDatabase Sqlite => Database as SqliteDatabase ?? throw new InvalidOperationException("These services run on " + Database.Provider + ", not on SQLite.");

    public CommandCounter Commands { get; } = new();

    /// <summary>Where a race test commits a racing command, in the middle of another.</summary>
    public CommandHook Hook { get; } = new();

    public ServiceProvider Provider { get; }

    public AsyncServiceScope Scope() => Provider.CreateAsyncScope();

    /// <summary>
    /// Provisions a tenant through the use cases, as system work outside any tenant: a tenant, its organization
    /// with the root, a role per pack and a first administrator.
    /// </summary>
    public async Task<HostTenancy.ProvisionedTenant> ProvisionAsync(
        string slug,
        TenantShape shape = TenantShape.Hierarchical,
        Guid? administrator = null,
        string administratorName = "Ada")
    {
        await using var scope = Scope();
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            return await scope.ServiceProvider.GetRequiredService<HostTenancy.TenantCommands>().ProvisionAsync(
                new HostTenancy.TenantToProvision(slug, Capitalized(slug) + " Works", shape, Capitalized(slug), administrator ?? Guid.NewGuid(), administratorName),
                CancellationToken.None);
        }
    }

    /// <summary>Adds a unit, as system work in the tenant, with the application's fields set by <paramref name="configure"/>.</summary>
    public Task<OrganizationUnitId> AddUnitAsync(TenantId tenant, OrganizationUnitId parent, string name, Action<HostUnit>? configure = null)
        => BySystemIn(tenant, services => services.Organization().AddUnitAsync(parent, name, CancellationToken.None, configure: configure));

    /// <summary>Adds a seat, as system work in the tenant.</summary>
    public Task<SeatId> AddSeatAsync(TenantId tenant, Guid identity, string displayName)
        => BySystemIn(tenant, services => services.Seats().AddSeatAsync(identity, displayName, CancellationToken.None));

    /// <summary>
    /// A new seat, added, placed at <paramref name="unit"/> as its primary placement and granted there the role
    /// of each pack, by system work, one command at a time.
    /// </summary>
    public async Task<SeatId> SeatAtAsync(HostTenancy.ProvisionedTenant tenant, string displayName, OrganizationUnitId unit, params string[] packs)
    {
        var seat = await AddSeatAsync(tenant.Tenant, Guid.NewGuid(), displayName);
        await BySystemIn(tenant.Tenant, services => services.Seats().PlaceAsync(seat, unit, primary: true, CancellationToken.None));
        foreach (var pack in packs)
        {
            await GrantAsync(tenant, seat, unit, pack);
        }

        return seat;
    }

    /// <summary>Grants a seat the role of a pack at a unit where it is placed, by system work.</summary>
    public Task GrantAsync(HostTenancy.ProvisionedTenant tenant, SeatId seat, OrganizationUnitId unit, string pack, DateTimeOffset? until = null, DateTimeOffset? from = null)
        => BySystemIn(tenant.Tenant, services => services.Seats().GrantAsync(seat, unit, tenant.RolesByPack[pack], until, reason: null, CancellationToken.None, from));

    /// <summary>Runs <paramref name="act"/> as <paramref name="caller"/>, in a scope of its own.</summary>
    public async Task<T> RunAsync<T>(ITenancyCaller caller, Func<IServiceProvider, Task<T>> act)
    {
        await using var scope = Scope();
        using (TenancyCallers.Begin(caller))
        {
            return await act(scope.ServiceProvider);
        }
    }

    /// <summary>Runs <paramref name="act"/> as <paramref name="caller"/>, in a scope of its own.</summary>
    public Task RunAsync(ITenancyCaller caller, Func<IServiceProvider, Task> act)
        => RunAsync(caller, async services =>
        {
            await act(services);
            return true;
        });

    /// <summary>Runs <paramref name="act"/> as system work in <paramref name="tenant"/>, in a scope of its own.</summary>
    public Task<T> BySystemIn<T>(TenantId tenant, Func<IServiceProvider, Task<T>> act) => RunAsync(HostCaller.SystemIn(tenant), act);

    /// <summary>Runs <paramref name="act"/> as system work in <paramref name="tenant"/>, in a scope of its own.</summary>
    public Task BySystemIn(TenantId tenant, Func<IServiceProvider, Task> act) => RunAsync(HostCaller.SystemIn(tenant), act);

    /// <summary>Runs <paramref name="act"/> as a seat, in a scope of its own.</summary>
    public Task<T> BySeat<T>(TenantId tenant, SeatId seat, Func<IServiceProvider, Task<T>> act) => RunAsync(HostCaller.InSeat(tenant, seat), act);

    /// <summary>Runs <paramref name="act"/> as a seat, in a scope of its own.</summary>
    public Task BySeat(TenantId tenant, SeatId seat, Func<IServiceProvider, Task> act) => RunAsync(HostCaller.InSeat(tenant, seat), act);

    /// <summary>
    /// Accepts an invitation as a person signed in with <paramref name="identity"/>, in a scope of its own: the
    /// toolkit's caller is the user, and no Tenancy caller is begun, as in a request that names no tenant.
    /// </summary>
    public async Task<HostTenancy.AcceptedInvitation> AcceptAsync(Guid identity, string token, string? displayName = "Wren", string? verifiedAddress = null)
    {
        await using var scope = Scope();
        using (DDDToolkit.Access.Callers.Begin(DDDToolkit.Abstractions.Access.Caller.User(identity)))
        using (TenancyCallers.BeginNone())
        {
            return await scope.ServiceProvider.Invitations().AcceptAsync(token, displayName, verifiedAddress, CancellationToken.None);
        }
    }

    /// <summary>The stored rights rows, of every tenant or of one seat, straight from the table past every filter.</summary>
    public async Task<List<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>> StoredRightsAsync(SeatId? seat = null)
    {
        await using var scope = Scope();
        var rights = scope.ServiceProvider.Tenancy().Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().IgnoreQueryFilters().AsNoTracking();
        if (seat is { } only)
        {
            rights = rights.Where(right => right.SeatId == only);
        }

        return await rights.ToListAsync();
    }

    /// <summary>The stored closure of one tenant's organization, straight from the table past every filter.</summary>
    public async Task<List<OrganizationUnitPath<TenantId, OrganizationUnitId>>> StoredPathsAsync(TenantId tenant)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.Tenancy().Set<OrganizationUnitPath<TenantId, OrganizationUnitId>>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(path => path.TenantId == tenant)
            .ToListAsync();
    }

    public void Dispose()
    {
        Provider.Dispose();
        if (_ownsDatabase)
        {
            Database.Dispose();
        }
    }

    private void Configure(DbContextOptionsBuilder options, IServiceProvider provider, Wiring wiring)
    {
        Database.Use(options);

        switch (wiring)
        {
            case Wiring.Wired:
                options.UseDDDToolkit(provider);
                break;
            case Wiring.WithoutTenancy:
                options.UseDDDToolkitCore(provider);
                break;
            case Wiring.TenancyFirst:
                options.UseTenancy(provider).UseDDDToolkitCore(provider);
                break;
            case Wiring.WrittenOut:
                options.UseDDDToolkit(provider).UseTenancy(provider);
                break;
            case Wiring.OneByOne:
                options.UseDDDToolkitCore(provider).UseTenancy(provider);
                break;
        }

        options.AddInterceptors(Commands, Hook, Hook.Saves);
    }

    private static string Capitalized(string slug) => char.ToUpperInvariant(slug[0]) + slug[1..];
}

/// <summary>What a test resolves from a scope, by name.</summary>
public static class ScopedServices
{
    public static HostTenancy.TenantCommands Tenants(this IServiceProvider services) => services.GetRequiredService<HostTenancy.TenantCommands>();

    public static HostTenancy.OrganizationCommands Organization(this IServiceProvider services) => services.GetRequiredService<HostTenancy.OrganizationCommands>();

    public static HostTenancy.SeatCommands Seats(this IServiceProvider services) => services.GetRequiredService<HostTenancy.SeatCommands>();

    public static HostTenancy.RoleCommands Roles(this IServiceProvider services) => services.GetRequiredService<HostTenancy.RoleCommands>();

    public static HostTenancy.TenancyDirectory Directory(this IServiceProvider services) => services.GetRequiredService<HostTenancy.TenancyDirectory>();

    public static HostTenancy.InvitationCommands<HostInvitation, InvitationId> Invitations(this IServiceProvider services)
        => services.GetRequiredService<HostTenancy.InvitationCommands<HostInvitation, InvitationId>>();

    public static ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId> Answers(this IServiceProvider services)
        => services.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>();

    public static TestTenancyContext Tenancy(this IServiceProvider services) => services.GetRequiredService<TestTenancyContext>();

    public static TestWidgetContext Widgets(this IServiceProvider services) => services.GetRequiredService<TestWidgetContext>();
}
