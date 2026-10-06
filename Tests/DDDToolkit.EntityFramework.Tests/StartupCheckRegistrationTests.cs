using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Messaging.Postgres;
using DDDToolkit.Startup;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Each registration of the Entity Framework packages brings the start-up checks of what it registers, once
/// however often it is called, in the stage that says when it can run: a host gets them without naming one. And
/// what they look at: every registered context, but a context that maps none of the toolkit's classes needs none of
/// its interceptors, one that is not on Postgres none of the checks of Postgres, and one given the toolkit's base alone
/// runs as the login role on purpose.
/// </summary>
public sealed class StartupCheckRegistrationTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Each_registration_brings_its_checks_once_in_their_stages()
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddDDDToolkitEntityFramework();
        services.AddSupabaseRowLevelSecurity();
        services.AddPostgresRowLevelSecurity();
        services.AddSupabaseMigrations(SupabaseMigrationSource.For(() => SupabaseShelfContext.Create()));
        services.AddSupabaseMigrations(SupabaseMigrationSource.For(() => SupabaseLedgerContext.Create()));

        services.GetStartupChecks().InOrder().Select(check => (check.Name, check.Stage)).Should().Equal(
            (EntityFrameworkChecks.ToolkitWiredCheck, StartupCheckStage.Services),
            (PostgresRowAccessChecks.RowLevelSecurityWiredCheck, StartupCheckStage.Services),
            (PostgresRowAccessChecks.LoginRoleMaySwitchToCallersCheck, StartupCheckStage.Login),
            (SupabaseMigrations.AppliedCheck, StartupCheckStage.Migrations),
            (PostgresRowAccessChecks.LoginRoleOwnsNothingCheck, StartupCheckStage.Database),
            (PostgresRowAccessChecks.DefinerOwnersBypassCheck, StartupCheckStage.Database));

        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IHostedService),
            "none of them runs until the host asks for its checks: an application that upgrades keeps the start-up it had");
    }

    [Fact]
    public void A_pgmq_sink_or_consumer_brings_its_check_on_by_default_as_it_was()
    {
        var services = new ServiceCollection();
        var queues = NpgsqlDataSource.Create("Host=localhost;Database=shop");

        services.AddPgmqSink(queues, pgmq => pgmq.UseQueue("shop"));
        services.AddPgmqConsumer(queues, "storefront");

        services.GetStartupChecks().Registered.Should().ContainSingle().Which.Should().Match<StartupCheck>(check =>
            check.Name == PgmqQueue.ExtensionInstalledCheck && check.OnByDefault && check.Stage == StartupCheckStage.Database);
        services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name == "StartupCheckRunner")
            .Should().ContainSingle("the check ran in every host with a sink or a consumer before the checks were run together, and still does");
    }

    [Fact]
    public async Task A_context_built_without_the_toolkit_stops_the_start_and_one_that_maps_none_of_its_classes_does_not()
    {
        _db.EnsureCreated(() => _db.CreateLibraryContext());

        // The library maps aggregates of the toolkit's: without UseDDDToolkit it saves, and checks nothing.
        var bare = () => StartAsync(services => services.AddDbContext<LibraryContext>(options => options.UseSqlite(_db.Connection)));

        (await bare.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().StartWith("'LibraryContext' was built without the DDDToolkit interceptors");

        // A context a library brings for its own tables needs none of the interceptors, and is passed over; so is
        // every context that is not on Postgres by the checks of row level security, which are registered here too.
        using var host = await StartAsync(services => services
            .AddDbContext<LibraryContext>((provider, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider))
            .AddDbContext<NotesOfALibrary>(options => options.UseSqlite(_db.Connection)));
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task The_check_of_row_level_security_takes_a_context_given_the_base_alone_as_meant_and_stops_one_without_the_toolkit()
    {
        // On Postgres, which the check asks without connecting: UseDDDToolkit would have run the context as its caller,
        // so UseDDDToolkitCore without row level security is the application saying it runs as the login role.
        await RowLevelSecurityWiredAsync(options => options.UseNpgsql("Host=nowhere.invalid;Database=unused"), (provider, options) => options.UseDDDToolkitCore(provider));
        await RowLevelSecurityWiredAsync(options => options.UseNpgsql("Host=nowhere.invalid;Database=unused"), (provider, options) => options.UseDDDToolkit(provider));

        // Without the toolkit at all nothing says the login role was meant.
        var bare = () => RowLevelSecurityWiredAsync(options => options.UseNpgsql("Host=nowhere.invalid;Database=unused"), (_, _) => { });
        (await bare.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should()
            .StartWith("'SupabaseShelfContext' does not run its commands as the caller").And.Contain("UseDDDToolkitCore(serviceProvider), which says so");
    }

    /// <summary>
    /// Runs the check <see cref="PostgresRowAccessChecks.RowLevelSecurityWiredCheck"/> alone, which opens no connection,
    /// over a host with row level security registered and a context on <paramref name="database"/> wired by
    /// <paramref name="wire"/>.
    /// </summary>
    private static async Task RowLevelSecurityWiredAsync(Action<DbContextOptionsBuilder> database, Action<IServiceProvider, DbContextOptionsBuilder> wire)
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddPostgresRowLevelSecurity();
        services.AddDbContext<SupabaseShelfContext>((provider, options) =>
        {
            database(options);
            wire(provider, options);
        });

        var check = services.GetStartupChecks().Registered.Single(registered => registered.Name == PostgresRowAccessChecks.RowLevelSecurityWiredCheck);
        await using var host = services.BuildServiceProvider();
        await check.RunAsync(host, Cancellation);
    }

    /// <summary>
    /// A host with the toolkit and row level security registered, and <paramref name="configure"/>, that runs its
    /// start-up checks: started, or what stopped it thrown.
    /// </summary>
    private static async Task<IHost> StartAsync(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddDDDToolkitEntityFramework();
        builder.Services.AddPostgresRowLevelSecurity();
        builder.Services.RunStartupChecks();
        configure(builder.Services);

        var host = builder.Build();
        try
        {
            await host.StartAsync(Cancellation);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>A note a library keeps of its own: a class of the library's, and of none of the toolkit's.</summary>
    private sealed class Note
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>A context that maps none of the toolkit's classes, as one a library brings for its own tables.</summary>
    private sealed class NotesOfALibrary(DbContextOptions<NotesOfALibrary> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();
    }
}
