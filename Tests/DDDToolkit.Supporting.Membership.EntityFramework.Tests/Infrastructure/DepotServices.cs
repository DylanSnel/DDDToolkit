using DDDToolkit.EntityFramework;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The depot's services over one database: the toolkit's Entity Framework integration, the two kinds of
/// resource that stand beside the depot registered through the TestHost, each with the class that answers
/// for the depot, and the depot's context on that database. Which database, and what else a context is given,
/// is the test's to say, so the same services run on SQLite and on Postgres.
/// </summary>
public sealed class DepotServices : IDisposable
{
    /// <param name="database">Points a context's options at the database.</param>
    /// <param name="clock">The clock the access questions ask.</param>
    /// <param name="configure">Registers what a test needs before the resources are registered.</param>
    /// <param name="ownContexts">
    /// Whether the host takes its contexts from a pool with a factory, as one does whose readings each take a
    /// context of their own, rather than registering the context alone.
    /// </param>
    /// <param name="wiring">What a context is given besides the toolkit's interceptors, such as the caller's role on every connection.</param>
    public DepotServices(
        Action<DbContextOptionsBuilder> database,
        TimeProvider clock,
        Action<IServiceCollection>? configure = null,
        bool ownContexts = false,
        Action<DbContextOptionsBuilder, IServiceProvider>? wiring = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Commands);
        services.AddSingleton(clock);
        configure?.Invoke(services);

        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, _, _) => Task.CompletedTask));
        DepotHost.Add(services);

        void Options(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            database(options);
            options.UseDDDToolkit(provider);
            wiring?.Invoke(options, provider);
            options.AddInterceptors(provider.GetRequiredService<CommandCounter>());
        }

        if (ownContexts)
        {
            services.AddPooledDbContextFactory<DepotContext>(Options);
            services.AddScopedFromPool<DepotContext>();
        }
        else
        {
            services.AddDbContext<DepotContext>(Options);
        }

        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Every command the contexts sent.</summary>
    public CommandCounter Commands { get; } = new();

    /// <summary>The depot's services.</summary>
    public ServiceProvider Provider { get; }

    /// <summary>Asks something as <paramref name="caller"/>, in a scope of its own.</summary>
    public async Task<T> AsAsync<T>(Caller caller, Func<IServiceProvider, Task<T>> ask)
    {
        using (Callers.Begin(caller))
        {
            await using var scope = Provider.CreateAsyncScope();
            return await ask(scope.ServiceProvider);
        }
    }

    /// <summary>Does something as <paramref name="caller"/>, in a scope of its own.</summary>
    public async Task AsAsync(Caller caller, Func<IServiceProvider, Task> work)
    {
        using (Callers.Begin(caller))
        {
            await using var scope = Provider.CreateAsyncScope();
            await work(scope.ServiceProvider);
        }
    }

    /// <summary>The access questions about pallets, asked as <paramref name="person"/>.</summary>
    public Task<T> PalletsAsync<T>(DepotPerson person, Func<IMemberQuestions<PalletId>, Task<T>> ask)
        => AsAsync(person.Caller, provider => ask(provider.GetRequiredService<IMemberQuestions<PalletId>>()));

    /// <summary>The access questions about crates, asked as <paramref name="person"/>.</summary>
    public Task<T> CratesAsync<T>(DepotPerson person, Func<IMemberQuestions<CrateId>, Task<T>> ask)
        => AsAsync(person.Caller, provider => ask(provider.GetRequiredService<IMemberQuestions<CrateId>>()));

    /// <summary>Changes the depot's own rows, or its resources, as the application's own work, and saves.</summary>
    public Task ChangeAsync(Func<DepotContext, DepotDesk, Task> change)
        => AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<DepotContext>();
            await change(context, provider.GetRequiredService<DepotDesk>());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    /// <inheritdoc />
    public void Dispose() => Provider.Dispose();
}
