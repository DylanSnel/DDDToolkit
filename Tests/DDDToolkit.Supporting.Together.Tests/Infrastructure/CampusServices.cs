using System.Globalization;
using System.Text.Json;
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;

namespace DDDToolkit.Supporting.Together.Tests.Infrastructure;

/// <summary>Somebody a question is asked as: a person who signed in, known by the identity their token carries.</summary>
/// <param name="Name">What the tests call them.</param>
/// <param name="Identity">The person's verified identity: the <c>sub</c> of their token.</param>
public sealed record CampusPerson(string Name, Guid Identity)
{
    /// <summary>The caller as the toolkit knows it: a signed-in user, with nothing of any college in its token.</summary>
    public Caller Caller => Callers.FromClaims(Claims);

    /// <summary>The claims of the person's token, as JSON: what a database is given for the caller as well.</summary>
    public string Claims => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["sub"] = Identity.ToString("D", CultureInfo.InvariantCulture),
        ["role"] = "authenticated",
    });

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Where a host takes the context of the courses and the labs from.</summary>
public enum CampusContexts
{
    /// <summary>The context is registered alone: one for a request, which everything in it reads on.</summary>
    OfTheRequest,

    /// <summary>From a pool with a factory, and one of them for a request: a host whose readings each take a context of their own.</summary>
    FromAPool,

    /// <summary>From a factory the host wrote itself, and none for a request: every reading takes a context of the factory's.</summary>
    FromAFactoryAlone,
}

/// <summary>
/// The campus' services over one database: the toolkit's Entity Framework integration, the application's own
/// registration of both supporting domains (<see cref="CampusHost"/>), and its two contexts on that database.
/// Which database, and what else a host on that database registers, is the test's to say, so the same
/// services run on SQLite and on Postgres.
/// <para>
/// A question is asked the way a request asks it: as a signed-in user, whose seat in the college the request
/// names is found through that user's identity, in a scope of its own.
/// </para>
/// </summary>
public sealed class CampusServices : IDisposable
{
    /// <summary>The scope of the courses' and the labs' own system work in a college.</summary>
    public const string Scope = CourseMembership.Scope;

    /// <param name="database">Points a context's options at the database.</param>
    /// <param name="clock">The clock both domains ask.</param>
    /// <param name="host">Registers what a host on this database adds after the application's own registration, such as row level security.</param>
    /// <param name="wiring">What a context is given besides the toolkit's and Tenancy's interceptors, such as the caller's role on every connection.</param>
    /// <param name="contexts">Where the host takes the courses' context from.</param>
    public CampusServices(
        Action<DbContextOptionsBuilder> database,
        TimeProvider clock,
        Action<IServiceCollection>? host = null,
        Action<DbContextOptionsBuilder, IServiceProvider>? wiring = null,
        CampusContexts contexts = CampusContexts.OfTheRequest)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Commands);
        services.AddSingleton(clock);

        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, _, _) => Task.CompletedTask));
        CampusHost.Add(services);
        host?.Invoke(services);

        void Options(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            database(options);
            options.UseDDDToolkit(provider).UseTenancy(provider);
            wiring?.Invoke(options, provider);
            options.AddInterceptors(provider.GetRequiredService<CommandCounter>());
        }

        services.AddDbContext<TenancyContext>(Options);
        switch (contexts)
        {
            case CampusContexts.FromAPool:
                services.AddPooledDbContextFactory<CampusContext>(Options);
                services.AddScopedFromPool<CampusContext>();
                break;

            case CampusContexts.FromAFactoryAlone:
                services.AddSingleton<IDbContextFactory<CampusContext>>(provider => new FactoryOfTheHosts(provider, Options));
                break;

            default:
                services.AddDbContext<CampusContext>(Options);
                break;
        }

        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Every command the contexts sent.</summary>
    public CommandCounter Commands { get; } = new();

    /// <summary>The application's services.</summary>
    public ServiceProvider Provider { get; }

    /// <summary>
    /// Asks something as <paramref name="person"/> in the college <paramref name="college"/> names, in a scope
    /// of its own, as a request does: the toolkit's caller is the signed-in user, and the Tenancy caller is
    /// what tenant selection finds for that user there, a seat or nobody.
    /// </summary>
    public async Task<T> AsAsync<T>(CampusPerson person, string? college, Func<IServiceProvider, Task<T>> ask)
    {
        var caller = person.Caller;
        using (Callers.Begin(caller))
        {
            await using var scope = Provider.CreateAsyncScope();
            var seat = await scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(caller, college, TestContext.Current.CancellationToken);
            using (TenancyCallers.Begin(seat))
            {
                return await ask(scope.ServiceProvider);
            }
        }
    }

    /// <summary>Does something as <paramref name="person"/> in the college <paramref name="college"/> names.</summary>
    public Task AsAsync(CampusPerson person, string? college, Func<IServiceProvider, Task> work)
        => AsAsync(person, college, async provider =>
        {
            await work(provider);
            return true;
        });

    /// <summary>The access questions about courses, asked as <paramref name="person"/> in a college.</summary>
    public Task<T> CoursesAsync<T>(CampusPerson person, string? college, Func<IMemberQuestions<CourseId>, Task<T>> ask)
        => AsAsync(person, college, provider => ask(provider.GetRequiredService<IMemberQuestions<CourseId>>()));

    /// <summary>The access questions about labs, asked as <paramref name="person"/> in a college.</summary>
    public Task<T> LabsAsync<T>(CampusPerson person, string? college, Func<IMemberQuestions<LabId>, Task<T>> ask)
        => AsAsync(person, college, provider => ask(provider.GetRequiredService<IMemberQuestions<LabId>>()));

    /// <summary>Runs <paramref name="work"/> as system work outside any tenant, in a scope of its own: what provisions a college.</summary>
    public async Task<T> BySystemAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await using var scope = Provider.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> as system work in <paramref name="tenant"/>, in a scope of its own: in
    /// Tenancy's scope for its use cases, or in the courses' and the labs' own for what they keep.
    /// </summary>
    public async Task<T> BySystemInAsync<T>(TenantId tenant, Func<IServiceProvider, Task<T>> work, string scope = TenancyWork.SystemScope)
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant, scope: scope))
        {
            await using var services = Provider.CreateAsyncScope();
            return await work(services.ServiceProvider);
        }
    }

    /// <summary>Runs <paramref name="work"/> as system work in <paramref name="tenant"/>.</summary>
    public Task BySystemInAsync(TenantId tenant, Func<IServiceProvider, Task> work, string scope = TenancyWork.SystemScope)
        => BySystemInAsync(
            tenant,
            async provider =>
            {
                await work(provider);
                return true;
            },
            scope);

    /// <summary>Changes the courses, their roles or the labs of <paramref name="tenant"/> as the application's own work there, and saves.</summary>
    public Task ChangeAsync(TenantId tenant, Func<CampusContext, Task> change)
        => BySystemInAsync(
            tenant,
            async provider =>
            {
                var context = provider.GetRequiredService<CampusContext>();
                await change(context);
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            },
            Scope);

    /// <inheritdoc />
    public void Dispose() => Provider.Dispose();

    /// <summary>A factory a host wrote itself: it makes a context when asked, and registers none.</summary>
    private sealed class FactoryOfTheHosts(IServiceProvider provider, Action<IServiceProvider, DbContextOptionsBuilder> configure) : IDbContextFactory<CampusContext>
    {
        public CampusContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<CampusContext>();
            configure(provider, options);
            return new CampusContext(options.Options);
        }
    }
}

/// <summary>What a test resolves from a scope, by name.</summary>
public static class ScopedServices
{
    public static CampusTenancy.TenantCommands Tenants(this IServiceProvider services) => services.GetRequiredService<CampusTenancy.TenantCommands>();

    public static CampusTenancy.OrganizationCommands Organization(this IServiceProvider services) => services.GetRequiredService<CampusTenancy.OrganizationCommands>();

    public static CampusTenancy.SeatCommands Seats(this IServiceProvider services) => services.GetRequiredService<CampusTenancy.SeatCommands>();

    public static CampusTenancy.RoleCommands Roles(this IServiceProvider services) => services.GetRequiredService<CampusTenancy.RoleCommands>();

    public static CampusContext Campus(this IServiceProvider services) => services.GetRequiredService<CampusContext>();
}
