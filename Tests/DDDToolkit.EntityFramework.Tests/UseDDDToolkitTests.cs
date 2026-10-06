using DDDToolkit.Access;
using DDDToolkit.Composition;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// One call wires a context for the toolkit. <c>UseDDDToolkit</c> adds the toolkit's own interceptors, which
/// <c>UseDDDToolkitCore</c> adds alone, and then every part the host's registrations brought, in their order: row
/// level security, which <c>AddPostgresRowLevelSecurity</c> and <c>AddSupabaseRowLevelSecurity</c> bring, on a context
/// on Postgres and on no other, and on one whose provider it cannot see yet, which it passes over at use where that
/// turns out not to be Postgres. The <c>Use...</c> calls of a chain written out as 3.1 wrote it add nothing twice, the
/// first time a context is given a part one information line says which, and a model that cannot do without a part is
/// refused at its first save where the context lacks it.
/// </summary>
public sealed class UseDDDToolkitTests : IDisposable
{
    /// <summary>The category of the information line: the toolkit's own class, which a test cannot name.</summary>
    private const string ContextParts = "DDDToolkit.EntityFramework.ContextParts";

    private const string Nowhere = "Host=nowhere.invalid;Database=unused";

    private static readonly Type[] TheToolkits =
        [typeof(PublishDomainEventsInterceptor), typeof(InvariantInterceptor), typeof(AggregateVersionInterceptor), typeof(DatabaseRefusalInterceptor)];

    private static readonly Type[] WithRowLevelSecurity = [.. TheToolkits, typeof(PostgresRowLevelSecurityInterceptor)];

    private readonly SqliteDatabase _db = new();

    private readonly KeptLogLines _logs = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _logs.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void The_base_alone_adds_the_toolkits_interceptors_and_nothing_a_package_brings()
    {
        using var services = Services(registered => registered.AddSupabaseRowLevelSecurity());

        Interceptors(OnPostgres().UseDDDToolkitCore(services)).Should().Equal(TheToolkits, "row level security is registered, and the base leaves it out");
        Interceptors(OnPostgres().UseDDDToolkitCore(services).UseDDDToolkitCore(services)).Should().Equal(TheToolkits, "a second call adds nothing");
        _logs.Of(ContextParts).Should().BeEmpty("the base gives a context no part, so there is nothing to say");

        FluentActions.Invoking(() => OnPostgres().UseDDDToolkitCore(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ((DbContextOptionsBuilder)null!).UseDDDToolkit(services)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Where_no_registration_brings_a_part_the_one_call_is_the_base()
    {
        using var services = Services();

        Interceptors(OnPostgres().UseDDDToolkit(services)).Should().Equal(TheToolkits);
        Interceptors(OnSqlite().UseDDDToolkit(services)).Should().Equal(TheToolkits);
        _logs.Of(ContextParts).Should().BeEmpty();
    }

    [Fact]
    public void Row_level_security_goes_on_a_context_on_postgres_and_on_no_other()
    {
        using var services = Services(registered => registered.AddSupabaseRowLevelSecurity());

        Interceptors(OnPostgres().UseDDDToolkit(services)).Should().Equal(WithRowLevelSecurity, "after the toolkit's own interceptors");
        Interceptors(OnSqlite().UseDDDToolkit(services)).Should().Equal(TheToolkits, "row level security is Postgres's: a context on SQLite in the same host is passed over");
    }

    [Fact]
    public void A_chain_written_out_as_before_the_one_call_still_gives_each_interceptor_once()
    {
        using var services = Services(registered => registered.AddSupabaseRowLevelSecurity());

        Interceptors(OnPostgres().UseDDDToolkit(services).UseSupabaseRowLevelSecurity(services)).Should().Equal(WithRowLevelSecurity, "as 3.1 wrote it");
        Interceptors(OnPostgres().UseDDDToolkit(services).UsePostgresRowLevelSecurity(services)).Should().Equal(WithRowLevelSecurity);
        Interceptors(OnPostgres().UseDDDToolkit(services).UseDDDToolkit(services)).Should().Equal(WithRowLevelSecurity, "called twice");
        Interceptors(OnPostgres().UseDDDToolkitCore(services).UseSupabaseRowLevelSecurity(services)).Should().Equal(WithRowLevelSecurity, "the parts taken one by one");
        Interceptors(OnPostgres().UseSupabaseRowLevelSecurity(services).UseDDDToolkit(services)).Should().Equal(
            [typeof(PostgresRowLevelSecurityInterceptor), .. TheToolkits],
            "named first it stays where it was, once: an interceptor of the connection runs apart from those of the save");
    }

    [Fact]
    public async Task A_context_whose_provider_comes_after_the_one_call_runs_as_its_caller_on_postgres_and_is_passed_over_elsewhere()
    {
        var registered = new ServiceCollection();
        registered.AddDDDToolkitEntityFramework();
        registered.AddSupabaseRowLevelSecurity();
        registered.RequireExplicitCallers();

        // The chain 3.1 accepted, with the provider set in OnConfiguring, which runs after the options callback; and
        // a context on SQLite whose callback names its provider after the one call.
        registered.AddDbContext<SelfConfiguredContext>((provider, options) => options.UseDDDToolkit(provider).UsePostgresRowLevelSecurity(provider));
        registered.AddDbContext<LedgerContext>((provider, options) => options.UseDDDToolkit(provider).UseSqlite(_db.Connection));
        await using var services = registered.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = services.CreateAsyncScope();
        var selfConfigured = scope.ServiceProvider.GetRequiredService<SelfConfiguredContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<LedgerContext>();

        // Neither said where it was when the parts were applied, so both have row level security, once.
        Interceptors(selfConfigured).Should().Equal(WithRowLevelSecurity);
        Interceptors(ledger).Should().Equal(WithRowLevelSecurity);
        selfConfigured.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL", "OnConfiguring set it, after the callback");
        FluentActions.Invoking(() => PostgresRowAccessChecks.EnsureRowLevelSecurityWired(selfConfigured)).Should().NotThrow();

        // On Postgres it asks who is calling before anything connects: nobody, in a host that requires a caller.
        await FluentActions.Awaiting(() => selfConfigured.Database.OpenConnectionAsync(Cancellation)).Should().ThrowAsync<NoCallerException>(
            "the interceptor runs a context on Postgres as its caller, wherever its provider was configured");

        // On SQLite it passes the context over: nobody is asked, nothing is set, and the context reads and saves.
        await ledger.Database.EnsureCreatedAsync(Cancellation);
        ledger.Lines.Add(new LedgerLine { Id = Guid.NewGuid(), Text = "opened" });
        await ledger.SaveChangesAsync(Cancellation);
        (await ledger.Lines.CountAsync(Cancellation)).Should().Be(1);

        // Where the options do say, they decide, as before.
        Interceptors(OnSqlite().UseDDDToolkit(services)).Should().Equal(TheToolkits);
        Interceptors(new DbContextOptionsBuilder<SupabaseShelfContext>().UseDDDToolkitCore(services).UseNpgsql(Nowhere)).Should().Equal(TheToolkits, "the base asks nothing of the provider");
    }

    [Fact]
    public async Task A_model_that_cannot_do_without_a_part_is_refused_at_its_first_save_where_the_context_lacks_it()
    {
        // No registration brought the part: the registration that does is named.
        await using (var without = Services())
        {
            await using var audited = new AuditedContext(_db.Options<AuditedContext>(options => options.UseApplicationServiceProvider(without).UseDDDToolkit(without)));
            await audited.Database.EnsureCreatedAsync(Cancellation);
            audited.Lines.Add(new LedgerLine { Id = Guid.NewGuid(), Text = "unaudited" });
            await FluentActions.Awaiting(() => audited.SaveChangesAsync(Cancellation)).Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "'AuditedContext' cannot do without the part tests.audit, which its model requires, and its options have no Audit, so its saves would go without it. " +
                "No registration brought it. Register it with services.AddAudit(), and options.UseDDDToolkit(serviceProvider) puts it on every context.");
            FluentActions.Invoking(() => EntityFrameworkChecks.EnsureToolkitWired(audited)).Should().Throw<InvalidOperationException>().WithMessage("*No registration brought it*");
        }

        _db.CountRows("Lines").Should().Be(0, "the save was refused before anything was written");

        // Registered, and given the base alone: the calls that put it on are named.
        await using var withAudit = Services(registered => registered.AddContextPart(new ContextPart<DbContextOptionsBuilder>("tests.audit", 150, (options, _) => options.AddInterceptors(new Audit()))));
        await using (var core = new AuditedContext(_db.Options<AuditedContext>(options => options.UseApplicationServiceProvider(withAudit).UseDDDToolkitCore(withAudit))))
        {
            core.Lines.Add(new LedgerLine { Id = Guid.NewGuid(), Text = "unaudited" });
            await FluentActions.Awaiting(() => core.SaveChangesAsync(Cancellation)).Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "'AuditedContext' cannot do without the part tests.audit, which its model requires, and its options have no Audit, so its saves would go without it. " +
                "Configure the context with options.UseDDDToolkit(serviceProvider), which puts on every part the registrations brought; after options.UseDDDToolkitCore(serviceProvider), add options.UseAudit(serviceProvider).");
        }

        // The one call puts it on, and so does the base with the part's own call.
        await using (var wired = new AuditedContext(_db.Options<AuditedContext>(options => options.UseApplicationServiceProvider(withAudit).UseDDDToolkit(withAudit))))
        {
            wired.Lines.Add(new LedgerLine { Id = Guid.NewGuid(), Text = "audited" });
            await wired.SaveChangesAsync(Cancellation);
            FluentActions.Invoking(() => EntityFrameworkChecks.EnsureToolkitWired(wired)).Should().NotThrow();

            // Stated as text, so the model still goes into a migration's snapshot.
            wired.Model.FindAnnotation(ContextPartRequirements.AnnotationPrefix + "tests.audit")!.Value.Should().BeOfType<string>();
        }

        await using (var byItself = new AuditedContext(_db.Options<AuditedContext>(options => options.UseDDDToolkitCore(withAudit).AddInterceptors(new Audit()))))
        {
            byItself.Lines.Add(new LedgerLine { Id = Guid.NewGuid(), Text = "audited" });
            await byItself.SaveChangesAsync(Cancellation);
        }

        _db.CountRows("Lines").Should().Be(2);
    }

    [Fact]
    public void Each_registration_brings_its_part_once_and_a_part_of_ones_own_takes_its_place_by_position()
    {
        var registered = new ServiceCollection();
        registered.AddDDDToolkitEntityFramework();
        registered.AddSupabaseRowLevelSecurity();
        registered.AddPostgresRowLevelSecurity();
        registered.AddContextPart(new ContextPart<DbContextOptionsBuilder>("tests.audit", 150, (options, _) => options.AddInterceptors(new Audit())));
        registered.AddContextPart(new ContextPart<DbContextOptionsBuilder>("tests.first", -1, (options, _) => options.AddInterceptors(new First())));
        registered.AddContextPart(new ContextPart<DbContextOptionsBuilder>("tests.audit", 50, (_, _) => throw new InvalidOperationException("A second part of a name is not registered.")));

        registered.GetContextParts<DbContextOptionsBuilder>().InOrder().Select(part => (part.Name, part.Position)).Should().Equal(
            ("tests.first", -1), (PostgresRowLevelSecurityInterceptor.PartName, PostgresRowLevelSecurityInterceptor.PartPosition), ("tests.audit", 150));

        using var services = registered.BuildServiceProvider();
        Interceptors(OnPostgres().UseDDDToolkit(services)).Should().Equal(
            [.. TheToolkits, typeof(First), typeof(PostgresRowLevelSecurityInterceptor), typeof(Audit)],
            "every part comes after the toolkit's own interceptors, a lower position before a higher, whatever order they were registered in");
    }

    [Fact]
    public void The_first_time_a_context_is_given_a_part_one_information_line_names_the_context_and_the_parts()
    {
        using var services = Services(registered => registered.AddSupabaseRowLevelSecurity());

        OnPostgres().UseDDDToolkit(services);
        OnPostgres().UseDDDToolkit(services);
        OnSqlite().UseDDDToolkit(services);

        _logs.Of(ContextParts).Should().ContainSingle("once per context type, however many scopes build its options, and none for a context given no part")
            .Which.Should().Match<(string Category, LogLevel Level, string Message, Exception? Exception)>(line =>
                line.Level == LogLevel.Information &&
                line.Message == "UseDDDToolkit gave 'SupabaseShelfContext' what the host's registrations bring, after the toolkit's own interceptors: postgres.row-level-security. " +
                    "A context that should do without one is configured with UseDDDToolkitCore and the Use... calls of the parts it does want.");
    }

    [Fact]
    public async Task A_host_wires_each_of_its_contexts_with_the_one_call_whether_from_a_scope_or_from_a_pool()
    {
        var registered = new ServiceCollection();
        registered.AddDDDToolkitEntityFramework();
        registered.AddSupabaseRowLevelSecurity();
        registered.AddDbContext<LibraryContext>((provider, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider));
        registered.AddPooledDbContextFactory<SupabaseShelfContext>((provider, options) => options.UseNpgsql(Nowhere).UseDDDToolkit(provider));
        await using var services = registered.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = services.CreateAsyncScope();

        // A scope's provider for the one, the application's for the pool's, which builds its options once.
        var library = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        await using var shelves = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<SupabaseShelfContext>>().CreateDbContextAsync(Cancellation);

        Interceptors(library).Should().Equal(TheToolkits);
        Interceptors(shelves).Should().Equal(WithRowLevelSecurity);
        FluentActions.Invoking(() => EntityFrameworkChecks.EnsureToolkitWired(library)).Should().NotThrow();
        FluentActions.Invoking(() => PostgresRowAccessChecks.EnsureRowLevelSecurityWired(shelves)).Should().NotThrow();
    }

    /// <summary>A host's services with the toolkit, the lines it logs kept, and what <paramref name="configure"/> registers.</summary>
    private ServiceProvider Services(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Information));
        services.AddDDDToolkitEntityFramework();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>The options of a context on Postgres, which nothing here connects.</summary>
    private static DbContextOptionsBuilder<SupabaseShelfContext> OnPostgres() => new DbContextOptionsBuilder<SupabaseShelfContext>().UseNpgsql(Nowhere);

    /// <summary>The options of a context on SQLite.</summary>
    private DbContextOptionsBuilder<LibraryContext> OnSqlite() => new DbContextOptionsBuilder<LibraryContext>().UseSqlite(_db.Connection);

    private static IReadOnlyList<Type> Interceptors(DbContextOptionsBuilder options)
        => [.. (options.Options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? []).Select(interceptor => interceptor.GetType())];

    private static IReadOnlyList<Type> Interceptors(DbContext context)
        => [.. (context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? []).Select(interceptor => interceptor.GetType())];

    /// <summary>What a package of the application's own adds to every context, at a position of its own.</summary>
    private sealed class Audit : SaveChangesInterceptor;

    /// <summary>A line of a ledger: nothing of the toolkit's, so its context needs no interceptor but what a test gives it.</summary>
    public sealed class LedgerLine
    {
        public Guid Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>A context on whatever its options say, SQLite here.</summary>
    public sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
    {
        public DbSet<LedgerLine> Lines => Set<LedgerLine>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<LedgerLine>().ToTable("Lines").Property(line => line.Id).ValueGeneratedNever();
    }

    /// <summary>A context that sets its provider itself, in <c>OnConfiguring</c>, as many do: after the options callback.</summary>
    public sealed class SelfConfiguredContext(DbContextOptions<SelfConfiguredContext> options) : DbContext(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql(Nowhere);
            }
        }
    }

    /// <summary>A context whose model cannot do without the part <c>tests.audit</c>, as a package's mapping would say.</summary>
    public sealed class AuditedContext(DbContextOptions<AuditedContext> options) : DbContext(options)
    {
        public DbSet<LedgerLine> Lines => Set<LedgerLine>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LedgerLine>().ToTable("Lines").Property(line => line.Id).ValueGeneratedNever();
            modelBuilder.Model.RequireContextPart("tests.audit", typeof(Audit), "services.AddAudit()", "options.UseAudit(serviceProvider)");
        }
    }

    /// <summary>A part that asks to go before every other part.</summary>
    private sealed class First : SaveChangesInterceptor;
}
