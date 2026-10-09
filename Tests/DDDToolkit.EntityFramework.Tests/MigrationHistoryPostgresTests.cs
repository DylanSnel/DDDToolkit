using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The migration history on a real Postgres, the way Supabase gets it: the files the export writes from a design-time
/// factory, applied as the CLI applies them, and a host that checks at start-up that none is missing. With
/// <c>UseDDDToolkitDesignTime</c> in the factory, the files record the migrations in the module's own history, which
/// is where the host, wired with <c>UseDDDToolkit</c>, reads them. Without it they record them in <c>public</c>, and
/// the check says that the two look in different tables, rather than only that every migration is missing.
/// </summary>
public sealed class MigrationHistoryPostgresTests(ExplicitCallersPostgres postgres)
{
    private const string Nowhere = "Host=nowhere.invalid;Database=unused";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_files_the_export_writes_record_the_migrations_where_the_running_application_reads_them()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
        await ApplyAsync(connectionString, FactoryWith(designTimeCall: true));
        await using var host = Host(connectionString, factoryWithTheCall: true);

        var check = () => host.EnsureSupabaseMigrationsAppliedAsync(Cancellation);

        await check.Should().NotThrowAsync("the host reads the history the files wrote");
        (await ScalarAsync(connectionString, """SELECT pg_catalog.to_regclass('stockroom."__EFMigrationsHistory"') IS NOT NULL""")).Should().Be(true, "the module keeps its history in its schema");
        (await ScalarAsync(connectionString, """SELECT pg_catalog.to_regclass('public."__EFMigrationsHistory"') IS NULL""")).Should().Be(true, "and nothing keeps one in public");
    }

    [Fact]
    public async Task A_factory_that_records_the_history_elsewhere_is_named_when_the_migrations_look_missing()
    {
        // The files of a factory that leaves the toolkit's call out record the migration in public's history.
        var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
        await ApplyAsync(connectionString, FactoryWith(designTimeCall: false));
        await using var host = Host(connectionString, factoryWithTheCall: false);

        var check = () => host.EnsureSupabaseMigrationsAppliedAsync(Cancellation);

        var refused = (await check.Should().ThrowAsync<SupabaseMigrationsPendingException>()).Which;
        refused.Pending.Should().ContainSingle().Which.Migrations.Should().Equal(CreateStockroom.Id);
        refused.Message.Should().Contain(
                "StockroomContext reads its migration history from stockroom.\"__EFMigrationsHistory\", and its design-time factory, which the exported files are written with, " +
                "records each migration in public.\"__EFMigrationsHistory\": the files record their migrations where the application does not look.")
            .And.Contain(
                "Where those files were applied to a database already, keep the history where they record it, and name that table in the application's options: " +
                "MigrationsHistoryTable(HistoryRepository.DefaultTableName).")
            .And.Contain("Where none of them was applied anywhere yet, give the factory the application's history instead, UseDDDToolkitDesignTime()")
            .And.Contain("for a context marked [SupabaseMigrations], whose factory the build writes, write a factory of your own beside it that does: the build then writes none and uses yours.")
            .And.Contain("Then delete the files, export them again and reset the local database");

        // The first advice is the one for this database, whose files are applied: the application names the table they
        // record in, and the check finds every migration.
        await using var named = Host(connectionString, factoryWithTheCall: false, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName));
        var checkAgain = () => named.EnsureSupabaseMigrationsAppliedAsync(Cancellation);
        await checkAgain.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_module_without_rules_or_an_outbox_lets_the_bookkeeping_role_read_its_history_under_a_login_that_owns_nothing()
    {
        // Supabase's defaults: the system caller that checks the migrations switches to ddd_system, which reaches a
        // module's schema only where an access file let it in. The counter has no rule and none of the toolkit's
        // tables, and gets that file all the same.
        var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
        var directory = Path.Combine(Path.GetTempPath(), "ddd-counter-" + Guid.NewGuid().ToString("N"));
        var login = $"counter_api_{Guid.NewGuid():N}"[..30];
        try
        {
            using (var designTime = Counter())
            {
                SupabaseMigrations.Export(designTime, directory, new SupabaseMigrationOptions()).IsInSync.Should().BeTrue();
            }

            // Every file in the order the Supabase CLI applies them, as the owner; then a login role that owns nothing
            // and may switch to the roles the files are written for, as the login role's file grants them.
            foreach (var file in Directory.GetFiles(directory, "*.sql").Order(StringComparer.Ordinal))
            {
                await ExecuteAsync(connectionString, await File.ReadAllTextAsync(file, Cancellation));
            }

            await ExecuteAsync(connectionString, $"""
                CREATE ROLE {login} LOGIN NOINHERIT PASSWORD '{login}';
                GRANT anon, authenticated, ddd_system_in, ddd_system TO {login} WITH INHERIT FALSE;
                """);

            var asLogin = new NpgsqlConnectionStringBuilder(connectionString) { Username = login, Password = login, Pooling = false }.ConnectionString;
            await using var host = new ServiceCollection()
                .AddDDDToolkitEntityFramework()
                .AddSupabaseRowLevelSecurity()
                .AddDbContext<CounterContext>((provider, options) => options.UseNpgsql(asLogin).UseDDDToolkit(provider))
                .AddSupabaseMigrations(SupabaseMigrationSource.For(Counter))
                .BuildServiceProvider();

            var check = () => host.EnsureSupabaseMigrationsAppliedAsync(Cancellation);

            await check.Should().NotThrowAsync("the bookkeeping role reads the counter's history, which its access file gave it");
        }
        finally
        {
            await ExecuteAsync(connectionString, $"DROP OWNED BY {login}; DROP ROLE IF EXISTS {login};");
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>The counter as a design-time factory makes it, on Postgres, with the toolkit's call.</summary>
    private static CounterContext Counter()
        => new(new DbContextOptionsBuilder<CounterContext>().UseNpgsql(Nowhere).UseDDDToolkitDesignTime().Options);

    /// <summary>A stockroom as a design-time factory makes it, on Postgres, with the toolkit's call or without it.</summary>
    private static Func<StockroomContext> FactoryWith(bool designTimeCall) => () =>
    {
        var options = new DbContextOptionsBuilder<StockroomContext>().UseNpgsql(Nowhere);
        return new StockroomContext((designTimeCall ? options.UseDDDToolkitDesignTime() : options).Options);
    };

    /// <summary>
    /// A host with the stockroom on <paramref name="connectionString"/>, wired with the one call, and its start-up check;
    /// <paramref name="npgsql"/> is what its provider's options add.
    /// </summary>
    private static ServiceProvider Host(string connectionString, bool factoryWithTheCall, Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
        => new ServiceCollection()
            .AddDDDToolkitEntityFramework()
            .AddDbContext<StockroomContext>((provider, options) => options.UseNpgsql(connectionString, npgsql).UseDDDToolkit(provider))
            .AddSupabaseMigrations(SupabaseMigrationSource.For(FactoryWith(factoryWithTheCall)))
            .BuildServiceProvider();

    /// <summary>Runs every file the export writes from <paramref name="factory"/>'s context, as the Supabase CLI runs them.</summary>
    private static async Task ApplyAsync(string connectionString, Func<StockroomContext> factory)
    {
        using var designTime = factory();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Cancellation);

        foreach (var file in SupabaseMigrations.Generate(designTime))
        {
            await using var command = new NpgsqlCommand(file.Sql, connection);
            await command.ExecuteNonQueryAsync(Cancellation);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Cancellation);
    }
}
