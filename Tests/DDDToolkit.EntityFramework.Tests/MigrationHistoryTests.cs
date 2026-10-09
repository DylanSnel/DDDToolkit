using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

// A host's own history repository derives from the provider's, which Npgsql keeps in a namespace of internal API, and
// the three-argument ReplaceService names it: what such a host writes, so the tests write it too.
#pragma warning disable EF1001

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Where a context keeps its migration history. Entity Framework keeps it in the provider's default schema unless the
/// options name another, so the modules of one database shared one history; the toolkit keeps it in the context's
/// default schema, beside its tables. <c>UseDDDToolkit</c>, <c>UseDDDToolkitCore</c> and a design-time factory's
/// <c>UseDDDToolkitDesignTime</c> agree, wherever the provider is configured; options that name the table keep it, a
/// model without a default schema keeps the provider's, a history repository the host put in places its history itself,
/// and options with an internal service provider of their own, which the toolkit cannot reach, name the table or are
/// refused. What is read is the SQL Entity Framework writes for the
/// history, so these are the tables a migration records itself in, and the ones the export's files record it in.
/// </summary>
public sealed partial class MigrationHistoryTests : IDisposable
{
    private const string Nowhere = "Host=nowhere.invalid;Database=unused";

    /// <summary>The history of a stockroom, where the toolkit keeps it.</summary>
    private const string InTheStockroom = "stockroom.\"__EFMigrationsHistory\"";

    /// <summary>The history in Postgres's default schema, where Entity Framework keeps it when nothing says otherwise.</summary>
    private const string InPublic = "\"__EFMigrationsHistory\"";

    private readonly SqliteDatabase _db = new();

    private readonly ServiceProvider _services = new ServiceCollection().AddDDDToolkitEntityFramework().BuildServiceProvider();

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void A_context_wired_with_UseDDDToolkit_keeps_its_migration_history_in_its_default_schema()
    {
        using var context = Stockroom(options => options.UseNpgsql(Nowhere).UseDDDToolkit(_services));

        HistoryOf(context).Should().Be(InTheStockroom);
        context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript().Should().Contain("CREATE TABLE IF NOT EXISTS " + InTheStockroom, "the history is made where it is read");
    }

    [Fact]
    public void A_context_given_the_base_alone_keeps_it_there_too()
    {
        // The history is the toolkit's own, not a package's part: leaving the parts out leaves it where it is.
        using var context = Stockroom(options => options.UseNpgsql(Nowhere).UseDDDToolkitCore(_services));

        HistoryOf(context).Should().Be(InTheStockroom);
    }

    [Fact]
    public void A_design_time_factory_with_UseDDDToolkitDesignTime_writes_the_script_the_running_context_writes()
    {
        using var running = Stockroom(options => options.UseNpgsql(Nowhere).UseDDDToolkit(_services));
        using var designTime = new StockroomContext(new DbContextOptionsBuilder<StockroomContext>().UseNpgsql(Nowhere).UseDDDToolkitDesignTime().Options);
        using var without = new StockroomContext(new DbContextOptionsBuilder<StockroomContext>().UseNpgsql(Nowhere).Options);

        HistoryOf(designTime).Should().Be(InTheStockroom, "the factory has no services, and needs none for this");
        designTime.GetService<IMigrator>().GenerateScript().Should().Be(running.GetService<IMigrator>().GenerateScript(), "dotnet ef writes what the application reads");

        // Without the call the factory's context keeps Entity Framework's default, and its scripts would record every
        // migration in a table the running application does not read.
        HistoryOf(without).Should().Be(InPublic);
        FluentActions.Invoking(() => ((DbContextOptionsBuilder)null!).UseDDDToolkitDesignTime()).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_files_the_export_writes_record_the_migration_in_the_modules_own_history()
    {
        using var designTime = new StockroomContext(new DbContextOptionsBuilder<StockroomContext>().UseNpgsql(Nowhere).UseDDDToolkitDesignTime().Options);

        var file = SupabaseMigrations.Generate(designTime).Should().ContainSingle().Subject;

        file.Sql.Should().Contain("CREATE TABLE IF NOT EXISTS " + InTheStockroom)
            .And.Contain($"INSERT INTO {InTheStockroom} (\"MigrationId\", \"ProductVersion\")\nVALUES ('{CreateStockroom.Id}'");
        file.Sql.Should().NotContain("INSERT INTO " + InPublic);
    }

    [Fact]
    public void Options_that_name_the_history_table_keep_it_where_they_name_it()
    {
        // Named before the toolkit's call, by name and schema.
        using var named = Stockroom(options => options
            .UseNpgsql(Nowhere, npgsql => npgsql.MigrationsHistoryTable("Applied", "audit"))
            .UseDDDToolkit(_services));

        // Named after it, by name alone: the way to keep the history in the provider's default schema, where a release
        // before the toolkit kept it for a context that named nothing.
        using var keptInPublic = Stockroom(options => options
            .UseDDDToolkit(_services)
            .UseNpgsql(Nowhere, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName)));

        HistoryOf(named).Should().Be("audit.\"Applied\"");
        HistoryOf(keptInPublic).Should().Be(InPublic);
    }

    [Fact]
    public void A_context_whose_model_names_no_schema_keeps_the_providers_default()
    {
        var options = new DbContextOptionsBuilder<NoticeboardContext>().UseNpgsql(Nowhere);
        options.UseDDDToolkit(_services);
        using var wired = new NoticeboardContext(options.Options);
        using var plain = new NoticeboardContext(new DbContextOptionsBuilder<NoticeboardContext>().UseNpgsql(Nowhere).Options);

        HistoryOf(wired).Should().Be(InPublic).And.Be(HistoryOf(plain));
    }

    [Fact]
    public void The_provider_may_be_configured_after_the_toolkit_or_by_the_context_itself()
    {
        using var providerAfter = Stockroom(options => options.UseDDDToolkit(_services).UseNpgsql(Nowhere));
        var options = new DbContextOptionsBuilder<SelfConfiguredStockroomContext>();
        options.UseDDDToolkit(_services);
        using var selfConfigured = new SelfConfiguredStockroomContext(options.Options);

        HistoryOf(providerAfter).Should().Be(InTheStockroom);
        HistoryOf(selfConfigured).Should().Be(InTheStockroom, "OnConfiguring set the provider, after the options callback");
    }

    [Fact]
    public async Task A_context_from_a_pool_keeps_it_there_at_every_rental()
    {
        var registered = new ServiceCollection();
        registered.AddDDDToolkitEntityFramework();
        registered.AddPooledDbContextFactory<StockroomContext>((provider, options) => options.UseNpgsql(Nowhere).UseDDDToolkit(provider));
        await using var services = registered.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var contexts = services.GetRequiredService<IDbContextFactory<StockroomContext>>();

        for (var rental = 0; rental < 2; rental++)
        {
            await using var context = await contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
            HistoryOf(context).Should().Be(InTheStockroom, "rental {0} of a pool, which builds its options once", rental);
        }
    }

    [Fact]
    public void On_SQLite_which_has_no_schemas_a_context_migrates_with_its_history_beside_its_tables()
    {
        using (var context = Stockroom(options => options.UseSqlite(_db.Connection).UseDDDToolkit(_services)))
        {
            context.Database.Migrate();
        }

        using var migrated = Stockroom(options => options.UseSqlite(_db.Connection).UseDDDToolkit(_services));
        migrated.Database.GetAppliedMigrations().Should().Equal(CreateStockroom.Id);
        migrated.Database.GetPendingMigrations().Should().BeEmpty();
        HistoryOf(migrated).Should().Be("\"__EFMigrationsHistory\"", "SQLite leaves out a schema's name, as it does for the tables");
        _db.CountRows(HistoryRepository.DefaultTableName).Should().Be(1);
    }

    [Fact]
    public void The_bookkeeping_role_is_granted_the_history_where_the_migrations_are_recorded()
    {
        using var designTime = new StockroomContext(new DbContextOptionsBuilder<StockroomContext>().UseNpgsql(Nowhere).UseDDDToolkitDesignTime().Options);
        var export = new RowAccessExport { Roles = RowAccessRoleNames.Default with { System = "ddd_system" }, WriteGrants = true };

        var script = PostgresRowAccess.Script(designTime, [], [], export);

        script.Should().Contain($"pg_catalog.to_regclass('{InTheStockroom}') IS NOT NULL")
            .And.Contain("GRANT USAGE ON SCHEMA stockroom TO ddd_system;")
            .And.Contain($"GRANT SELECT ON TABLE {InTheStockroom} TO ddd_system;");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void A_history_repository_the_host_put_in_with_ReplaceService_is_kept_in_either_form_and_order(bool forTheProvidersAlone, bool providerFirst)
    {
        // ReplaceService<IHistoryRepository, TReplacement>() replaces every registration of the service, and the
        // three-argument form only the provider's own, which Entity Framework recognizes by its type. Either way the
        // host's repository is made as Entity Framework makes it, and decides where its history is: here, where
        // Entity Framework keeps it when nothing says otherwise.
        using var context = Stockroom(options =>
        {
            if (providerFirst)
            {
                options.UseNpgsql(Nowhere);
            }

            options.UseDDDToolkit(_services);
            if (!providerFirst)
            {
                options.UseNpgsql(Nowhere);
            }

            if (forTheProvidersAlone)
            {
                options.ReplaceService<IHistoryRepository, NpgsqlHistoryRepository, HostsHistory>();
            }
            else
            {
                options.ReplaceService<IHistoryRepository, HostsHistory>();
            }
        });

        context.GetService<IHistoryRepository>().Should().BeOfType<HostsHistory>();
        HistoryOf(context).Should().Be(InPublic, "the host's own repository places the history, not the toolkit");
    }

    [Fact]
    public void The_bookkeeping_role_is_granted_the_history_a_repository_of_the_hosts_own_records_in()
    {
        // The grant follows the repository the context resolves, not a rule about the options beside it.
        using var context = Stockroom(options => options
            .UseNpgsql(Nowhere)
            .ReplaceService<IHistoryRepository, NpgsqlHistoryRepository, AuditedHistory>()
            .UseDDDToolkitDesignTime());
        var export = new RowAccessExport { Roles = RowAccessRoleNames.Default with { System = "ddd_system" }, WriteGrants = true };

        var script = PostgresRowAccess.Script(context, [], [], export);

        HistoryOf(context).Should().Be("\"Audit\".\"__EFMigrationsHistory\"");
        script.Should().Contain("pg_catalog.to_regclass('\"Audit\".\"__EFMigrationsHistory\"') IS NOT NULL")
            .And.Contain("GRANT USAGE ON SCHEMA \"Audit\" TO ddd_system;")
            .And.Contain("GRANT SELECT ON TABLE \"Audit\".\"__EFMigrationsHistory\" TO ddd_system;")
            .And.NotContain(InTheStockroom);
    }

    [Fact]
    public void Options_with_an_internal_service_provider_of_their_own_are_refused_unless_they_name_the_history()
    {
        // Entity Framework hands no extension the services of a provider it did not build, so the toolkit cannot place
        // the history there, and the factory, which has no such provider, would keep it elsewhere. That is said, rather
        // than left to split.
        using var internalServices = new ServiceCollection().AddEntityFrameworkNpgsql().BuildServiceProvider();
        using var named = Stockroom(options => options
            .UseNpgsql(Nowhere, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, StockroomContext.Schema))
            .UseInternalServiceProvider(internalServices)
            .UseDDDToolkit(_services));

        // Entity Framework checks such options as the context is made.
        FluentActions.Invoking(() => Stockroom(options => options.UseNpgsql(Nowhere).UseInternalServiceProvider(internalServices).UseDDDToolkit(_services)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*internal service provider of their own (UseInternalServiceProvider)*MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema)*");
        HistoryOf(named).Should().Be(InTheStockroom, "options that name the history hold for the host's provider and the factory alike");
    }

    /// <summary>The table the context's history repository records a migration in, as its SQL names it.</summary>
    private static string HistoryOf(DbContext context)
    {
        var insert = context.GetService<IHistoryRepository>().GetInsertScript(new HistoryRow("20260101000000_Probe", "10.0.0"));
        return InsertInto().Match(insert) is { Success: true } match
            ? match.Groups["table"].Value
            : throw new InvalidOperationException("The history repository wrote no INSERT INTO: " + insert);
    }

    /// <summary>A stockroom's context, its options written by <paramref name="configure"/>.</summary>
    private static StockroomContext Stockroom(Action<DbContextOptionsBuilder<StockroomContext>> configure)
    {
        var options = new DbContextOptionsBuilder<StockroomContext>();
        configure(options);
        return new StockroomContext(options.Options);
    }

    [GeneratedRegex(@"INSERT INTO (?<table>\S+) \(")]
    private static partial Regex InsertInto();

    /// <summary>A stockroom that sets its provider itself, in <c>OnConfiguring</c>, after the options callback.</summary>
    public sealed class SelfConfiguredStockroomContext(DbContextOptions<SelfConfiguredStockroomContext> options) : DbContext(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql(Nowhere);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.HasDefaultSchema(StockroomContext.Schema).Entity<StockroomBin>().ToTable("Bins");
    }

    /// <summary>A history repository of the host's own, which places nothing itself: Npgsql's, as Entity Framework makes it.</summary>
    public sealed class HostsHistory(HistoryRepositoryDependencies dependencies) : NpgsqlHistoryRepository(dependencies);

    /// <summary>A history repository of the host's own that keeps the history in a schema it names itself.</summary>
    public sealed class AuditedHistory(HistoryRepositoryDependencies dependencies) : NpgsqlHistoryRepository(dependencies)
    {
        protected override string? TableSchema => "Audit";
    }
}
