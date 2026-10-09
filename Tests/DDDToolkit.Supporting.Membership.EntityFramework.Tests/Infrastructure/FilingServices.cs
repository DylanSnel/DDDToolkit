using System.Globalization;
using System.Text.Json;
using DDDToolkit.EntityFramework;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// An application's services over one database: the toolkit's Entity Framework integration, both kinds of
/// resource registered through the TestHost, and the application's context on that database. Which database,
/// and what else a context is given, is the test's to say, so the same services run on SQLite and on Postgres.
/// <para>
/// The helpers run each piece of work as one caller in a scope of its own, the way a request does.
/// </para>
/// </summary>
public sealed class FilingServices : IDisposable
{
    /// <param name="database">Points a context's options at the database.</param>
    /// <param name="clock">The clock the access questions ask.</param>
    /// <param name="configure">Registers what a test needs before the resources are registered.</param>
    /// <param name="ownContexts">
    /// Whether the host takes its contexts from a pool with a factory (<c>AddPooledDbContextFactory</c> with
    /// <c>AddScopedFromPool</c>), as one does whose readings each take a context of their own, rather than
    /// registering the context alone (<c>AddDbContext</c>).
    /// </param>
    /// <param name="wiring">
    /// How a context is wired once it is on the database: <c>UseDDDToolkit</c>, which brings what the services
    /// registered, row level security among them, unless the test says otherwise, as <c>UseDDDToolkitCore</c> does
    /// for a context that runs as the login role.
    /// </param>
    public FilingServices(
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

        // The host's own events, raised from what the member list answered, are handed to it where the change is saved.
        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, events, _) =>
        {
            Raised.AddRange(events);
            return Task.CompletedTask;
        }));
        FilingHost.Add(services);

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
            // A pool builds the options once, with the application's services, and the scope's own context is
            // one of the pool's, bound to that scope and given back with it.
            services.AddPooledDbContextFactory<FilingContext>(Options);
            services.AddScopedFromPool<FilingContext>();
        }
        else
        {
            services.AddDbContext<FilingContext>(Options);
        }

        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Every command the contexts sent.</summary>
    public CommandCounter Commands { get; } = new();

    /// <summary>Every domain event a saved change raised, in order.</summary>
    public List<IDomainEvent> Raised { get; } = [];

    /// <summary>The application's services.</summary>
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

    /// <summary>
    /// Changes one document as the application's own work, through its aggregate, and saves it: what a handler
    /// does once its request passed the check.
    /// </summary>
    public Task ChangeAsync(DocumentId id, Action<Document> change)
        => AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken);
            change(document);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    /// <summary>Changes one folder as the application's own work, through its aggregate, and saves it.</summary>
    public Task ChangeAsync(FolderId id, Action<Folder> change)
        => AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var folder = await context.Folders.SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken);
            change(folder);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    /// <summary>
    /// Sends a request to share a document as <paramref name="caller"/>, the way a host's dispatcher does: the
    /// checks of the module first, and its handler only when they let the request through.
    /// </summary>
    public Task SendAsync(Caller caller, ShareDocument command)
        => AsAsync(caller, async provider =>
        {
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, TestContext.Current.CancellationToken);
            await provider.GetRequiredService<ShareDocumentHandler>().HandleAsync(command, TestContext.Current.CancellationToken);
        });

    /// <summary>Sends a request to put staff on a folder as <paramref name="caller"/>: the checks first, then its handler.</summary>
    public Task SendAsync(Caller caller, AdmitStaff command)
        => AsAsync(caller, async provider =>
        {
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, TestContext.Current.CancellationToken);
            await provider.GetRequiredService<AdmitStaffHandler>().HandleAsync(command, TestContext.Current.CancellationToken);
        });

    /// <summary>Reads one document with its shares and their roles, on a context of its own, so nothing comes from a change tracker.</summary>
    public Task<Document> ReadAsync(DocumentId id)
        => AsAsync(Caller.System, provider => provider.GetRequiredService<FilingContext>().Documents.AsNoTracking().SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken));

    /// <summary>Reads one folder with its staff and their roles, on a context of its own.</summary>
    public Task<Folder> ReadAsync(FolderId id)
        => AsAsync(Caller.System, provider => provider.GetRequiredService<FilingContext>().Folders.AsNoTracking().SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken));

    /// <inheritdoc />
    public void Dispose() => Provider.Dispose();
}

/// <summary>The callers of the tests, each as a host makes one: from the claims of a validated token.</summary>
public static class TestCallers
{
    /// <summary>The claim a member of staff is known by, as the folder's rules name it.</summary>
    public const string StaffClaim = "app_metadata.staff";

    /// <summary>A signed-in user: who a document's members are.</summary>
    public static Caller User(UserId user) => Callers.FromClaims(Claims(user.Value, staff: null));

    /// <summary>
    /// A signed-in member of staff, known by the code its token carries: who a folder's members are. The user
    /// id is somebody's, and no folder asks for it.
    /// </summary>
    public static Caller Staff(StaffCode staff, UserId? user = null) => Callers.FromClaims(Claims((user ?? UserId.CreateSequential()).Value, staff.Value));

    /// <summary>The claim a gardener's token says the garden its requests are in by, as the gardens' database reads it.</summary>
    public const string GardenClaim = "garden";

    /// <summary>
    /// The claims of a signed-in user's token, as JSON: what a database is given for the caller as well. A
    /// gardener's token says its garden too.
    /// </summary>
    public static string Claims(Guid user, string? staff, Guid? garden = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = user.ToString("D", CultureInfo.InvariantCulture), ["role"] = "authenticated" };
        if (staff is not null)
        {
            claims["app_metadata"] = new Dictionary<string, object> { ["staff"] = staff };
        }

        if (garden is { } within)
        {
            claims[GardenClaim] = within.ToString("D", CultureInfo.InvariantCulture);
        }

        return JsonSerializer.Serialize(claims);
    }
}
