using DDDToolkit.EntityFramework;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The gardens' services over one database: the toolkit's Entity Framework integration, the plots registered
/// through the TestHost, and the gardens' context on that database. Which database, and what else a context
/// is given, is the test's to say, so the same services run on SQLite and on Postgres.
/// <para>
/// A request is in a garden, which is the host's to know: the helpers that run something as somebody run it
/// in that person's garden, the way a host would read it from its caller.
/// </para>
/// </summary>
public sealed class GardenServices : IDisposable
{
    /// <param name="database">Points a context's options at the database.</param>
    /// <param name="clock">The clock the access questions ask.</param>
    /// <param name="configure">Registers what a test needs before the resource is registered.</param>
    /// <param name="ownContexts">
    /// Whether the host takes its contexts from a pool with a factory, as one does whose readings each take a
    /// context of their own, rather than registering the context alone.
    /// </param>
    /// <param name="wiring">
    /// How a context is wired once it is on the database: <c>UseDDDToolkit</c>, which brings what the services
    /// registered, row level security among them, unless the test says otherwise, as <c>UseDDDToolkitCore</c> does
    /// for a context that runs as the login role.
    /// </param>
    /// <param name="sheds">The rules the sheds are registered with; the host's own when left out.</param>
    public GardenServices(
        Action<DbContextOptionsBuilder> database,
        TimeProvider clock,
        Action<IServiceCollection>? configure = null,
        bool ownContexts = false,
        Action<DbContextOptionsBuilder, IServiceProvider>? wiring = null,
        MembershipRules? sheds = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Commands);
        services.AddSingleton(clock);
        configure?.Invoke(services);

        // The host's own events, raised from what a role's operations answered, are handed to it where the change is saved.
        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, events, _) =>
        {
            Raised.AddRange(events);
            return Task.CompletedTask;
        }));
        GardenHost.Add(services, sheds);

        void Options(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            database(options);
            // One call brings what the services registered, unless the test wires the context itself.
            if (wiring is null)
            {
                options.UseDDDToolkit(provider);
            }
            else
            {
                wiring(options, provider);
            }
            options.AddInterceptors(provider.GetRequiredService<CommandCounter>());
        }

        if (ownContexts)
        {
            services.AddPooledDbContextFactory<GardenContext>(Options);
            services.AddScopedFromPool<GardenContext>();
        }
        else
        {
            services.AddDbContext<GardenContext>(Options);
        }

        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Every command the contexts sent.</summary>
    public CommandCounter Commands { get; } = new();

    /// <summary>Every domain event a saved change raised, in order.</summary>
    public List<IDomainEvent> Raised { get; } = [];

    /// <summary>The gardens' services.</summary>
    public ServiceProvider Provider { get; }

    /// <summary>Asks something as <paramref name="caller"/>, in a scope of its own, and in no garden.</summary>
    public async Task<T> AsAsync<T>(Caller caller, Func<IServiceProvider, Task<T>> ask)
    {
        using (Callers.Begin(caller))
        {
            await using var scope = Provider.CreateAsyncScope();
            return await ask(scope.ServiceProvider);
        }
    }

    /// <summary>Does something as <paramref name="caller"/>, in a scope of its own, and in no garden.</summary>
    public async Task AsAsync(Caller caller, Func<IServiceProvider, Task> work)
    {
        using (Callers.Begin(caller))
        {
            await using var scope = Provider.CreateAsyncScope();
            await work(scope.ServiceProvider);
        }
    }

    /// <summary>Asks something as <paramref name="person"/>, in a scope of its own, in the garden its requests are in.</summary>
    public async Task<T> AsAsync<T>(GardenPerson person, Func<IServiceProvider, Task<T>> ask)
    {
        using (GardenOfTheRequest.Begin(person.Garden))
        {
            return await AsAsync(person.Caller, ask);
        }
    }

    /// <summary>The access questions about plots, asked as <paramref name="person"/>, in its garden.</summary>
    public Task<T> PlotsAsync<T>(GardenPerson person, Func<IMemberQuestions<PlotId>, Task<T>> ask)
        => AsAsync(person, provider => ask(provider.GetRequiredService<IMemberQuestions<PlotId>>()));

    /// <summary>Does the application's own work in <paramref name="garden"/>, in a scope of its own, and answers what it found.</summary>
    public async Task<T> InAsync<T>(GardenId garden, Func<IServiceProvider, Task<T>> work)
    {
        using (GardenOfTheRequest.Begin(garden))
        {
            return await AsAsync(Caller.System, work);
        }
    }

    /// <summary>Changes the gardens' rows as the application's own work, in <paramref name="garden"/> or in none, and saves.</summary>
    public async Task ChangeAsync(Func<GardenContext, Task> change, GardenId? garden = null)
    {
        using (garden is { } within ? GardenOfTheRequest.Begin(within) : null)
        {
            await AsAsync(Caller.System, async provider =>
            {
                var context = provider.GetRequiredService<GardenContext>();
                await change(context);
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            });
        }
    }

    /// <summary>Changes one role of plots through its aggregate, as the application's own work, and saves it.</summary>
    public Task ChangeAsync(PlotRoleId role, Action<PlotRole> change)
        => ChangeAsync(async context => change(await context.PlotRoles.SingleAsync(candidate => candidate.Id == role, TestContext.Current.CancellationToken)));

    /// <summary>Reads one plot with its gardeners and their roles, on a context of its own, so nothing comes from a change tracker.</summary>
    public Task<Plot> ReadAsync(PlotId id)
        => AsAsync(Caller.System, provider => provider.GetRequiredService<GardenContext>().Plots.AsNoTracking().SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken));

    /// <summary>Reads one role of plots on a context of its own, whichever garden it is of.</summary>
    public Task<PlotRole> ReadAsync(PlotRoleId id)
        => AsAsync(Caller.System, provider => provider.GetRequiredService<GardenContext>().PlotRoles.AsNoTracking().SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken));

    /// <inheritdoc />
    public void Dispose() => Provider.Dispose();
}
