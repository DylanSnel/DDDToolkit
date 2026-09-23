using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Entity Framework migrations exported as Supabase migration files. Everything but the last test
/// runs without a database, because the export never opens one.
/// </summary>
public sealed class SupabaseMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ddd-supabase-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// The name an exported file gets. The test assembly declares no [assembly: Module], so the module is
    /// the context's name without "Context".
    /// </summary>
    private static string FileOf(string migrationId, string module = "supabaseshelf") => $"{migrationId}.{module}.ddd.sql";

    private static IReadOnlyList<SupabaseMigrationFile> Generate(SupabaseMigrationOptions? options = null)
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Generate(context, options);
    }

    private SupabaseMigrationReport Export()
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Export(context, _directory);
    }

    private SupabaseMigrationReport Compare()
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Compare(context, _directory);
    }

    [Fact]
    public void Module_names_are_lower_case_letters_digits_and_dashes()
    {
        SupabaseMigrations.NormalizeModuleName("Ordering").Should().Be("ordering");
        SupabaseMigrations.NormalizeModuleName(" Order Management.Core ").Should().Be("order-management-core");
        SupabaseMigrations.NormalizeModuleName("___").Should().Be("module");
        SupabaseMigrations.ModuleNameOf(typeof(SupabaseShelfContext)).Should().Be("supabaseshelf", "without [assembly: Module] the context name stands in");
    }

    [Fact]
    public void A_source_takes_the_module_it_is_given_over_the_one_it_would_derive()
    {
        SupabaseMigrationSource.For(() => SupabaseShelfContext.Create(), "Library Shelves").Module.Should().Be("library-shelves");
        SupabaseMigrationSource.For(() => SupabaseShelfContext.Create()).Module.Should().Be("supabaseshelf");
    }

    [Fact]
    public void Each_migration_becomes_one_file_named_after_its_id_so_the_versions_are_the_timestamps()
    {
        var files = Generate();

        files.Select(f => f.FileName).Should().Equal(FileOf(CreateShelves.Id), FileOf(AddShelfCapacity.Id));
        files.Select(f => f.Version).Should().Equal("20260901120000", "20260915093000");
    }

    [Fact]
    public void The_first_file_creates_the_history_table_and_records_itself_in_it()
    {
        var sql = Generate()[0].Sql;

        sql.Should().StartWith($"-- Exported by DDDToolkit from the Entity Framework migration {CreateShelves.Id} of SupabaseShelfContext.\n");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"");
        sql.Should().Contain("CREATE TABLE \"Shelves\"");
        sql.Should().Contain("CREATE TABLE ddd.\"OutboxMessages\"");
        sql.Should().Contain($"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\")\nVALUES ('{CreateShelves.Id}'");
    }

    [Fact]
    public void A_later_file_holds_only_its_own_step()
    {
        var sql = Generate()[1].Sql;

        sql.Should().Contain("ALTER TABLE \"Shelves\" ADD \"Capacity\" integer NOT NULL DEFAULT 0;");
        sql.Should().Contain($"VALUES ('{AddShelfCapacity.Id}'");
        sql.Should().NotContain("CREATE TABLE");
        sql.Should().NotContain(CreateShelves.Id);
    }

    [Fact]
    public void There_is_no_transaction_in_the_file_because_the_cli_runs_each_file_in_one()
    {
        foreach (var file in Generate())
        {
            file.Sql.Should().NotContain("START TRANSACTION");
            file.Sql.Should().NotContain("COMMIT");
        }
    }

    [Fact]
    public void The_files_use_newlines_only_so_they_export_identically_on_every_platform()
        => Generate().Should().AllSatisfy(f => f.Sql.Should().NotContain("\r"));

    [Fact]
    public void A_new_table_in_public_gets_row_level_security_and_one_in_ddd_does_not()
    {
        var files = Generate();

        files[0].Sql.Should().Contain("ALTER TABLE \"Shelves\" ENABLE ROW LEVEL SECURITY;");
        files[0].Sql.Should().NotContain("\"OutboxMessages\" ENABLE ROW LEVEL SECURITY");
        files[1].Sql.Should().NotContain("ROW LEVEL SECURITY", "that migration creates no table");
    }

    [Fact]
    public void Row_level_security_follows_the_configured_schemas()
    {
        var none = Generate(new SupabaseMigrationOptions { RowLevelSecuritySchemas = new HashSet<string>() });
        none[0].Sql.Should().NotContain("ROW LEVEL SECURITY");

        var both = Generate(new SupabaseMigrationOptions { RowLevelSecuritySchemas = new HashSet<string> { "public", "ddd" } });
        both[0].Sql.Should().Contain("ALTER TABLE ddd.\"OutboxMessages\" ENABLE ROW LEVEL SECURITY;");
    }

    [Fact]
    public void Export_writes_every_missing_file_and_then_the_directory_is_in_sync()
    {
        var report = Export();

        report.Created.Select(e => e.MigrationId).Should().Equal(CreateShelves.Id, AddShelfCapacity.Id);
        report.IsInSync.Should().BeTrue();
        File.ReadAllText(Path.Combine(_directory, FileOf(CreateShelves.Id))).Should().Be(Generate()[0].Sql);

        Compare().Entries.Should().OnlyContain(e => e.Status == SupabaseMigrationStatus.Unchanged);
    }

    [Fact]
    public void Compare_writes_nothing_and_reports_the_missing_files()
    {
        var report = Compare();

        report.Entries.Should().OnlyContain(e => e.Status == SupabaseMigrationStatus.Missing);
        report.IsInSync.Should().BeFalse();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public void Export_only_adds_the_file_that_is_missing()
    {
        Export();
        File.Delete(Path.Combine(_directory, FileOf(AddShelfCapacity.Id)));

        var report = Export();

        report.Entries.Select(e => e.Status).Should().Equal(SupabaseMigrationStatus.Unchanged, SupabaseMigrationStatus.Created);
    }

    [Fact]
    public void A_changed_file_is_reported_and_never_overwritten()
    {
        Export();
        var path = Path.Combine(_directory, FileOf(CreateShelves.Id));
        File.AppendAllText(path, "-- edited by hand\n");

        var report = Export();

        report.Entries[0].Status.Should().Be(SupabaseMigrationStatus.Changed);
        File.ReadAllText(path).Should().EndWith("-- edited by hand\n");
    }

    [Fact]
    public void Windows_line_endings_in_a_checked_out_file_do_not_count_as_a_change()
    {
        Export();
        var path = Path.Combine(_directory, FileOf(CreateShelves.Id));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n", StringComparison.Ordinal));

        Compare().IsInSync.Should().BeTrue();
    }

    [Fact]
    public void A_file_exported_by_another_entity_framework_version_is_not_a_change()
    {
        Export();
        var path = Path.Combine(_directory, FileOf(CreateShelves.Id));
        var exported = File.ReadAllText(path);
        var older = System.Text.RegularExpressions.Regex.Replace(exported, $@"('{CreateShelves.Id}', )'[^']*'", "$1'9.0.0'");
        older.Should().NotBe(exported);
        File.WriteAllText(path, older);

        Compare().IsInSync.Should().BeTrue();
    }

    [Fact]
    public void A_file_of_another_name_with_the_same_version_blocks_the_export()
    {
        Directory.CreateDirectory(_directory);
        var other = Path.Combine(_directory, "20260901120000_something_else.sql");
        File.WriteAllText(other, "select 1;\n");

        var report = Export();

        report.Entries[0].Should().Be(new SupabaseMigrationEntry(CreateShelves.Id, SupabaseMigrationStatus.VersionTaken, other));
        File.Exists(Path.Combine(_directory, FileOf(CreateShelves.Id))).Should().BeFalse();
    }

    [Fact]
    public void An_exported_file_whose_migration_is_gone_is_orphaned_but_a_hand_written_one_is_left_alone()
    {
        Export();
        var removed = Path.Combine(_directory, "20260920100000_RemovedLater.sql");
        File.WriteAllText(removed, "-- Exported by DDDToolkit from the Entity Framework migration 20260920100000_RemovedLater of SupabaseShelfContext.\nselect 1;\n");
        File.WriteAllText(Path.Combine(_directory, "20260918000000_policies.sql"), "create policy p on \"Shelves\" for select using (true);\n");

        var report = Compare();

        report.Problems.Should().ContainSingle()
            .Which.Should().Be(new SupabaseMigrationEntry("20260920100000_RemovedLater", SupabaseMigrationStatus.Orphaned, removed));
    }

    [Fact]
    public void Two_contexts_share_one_directory_without_calling_each_others_files_orphans()
    {
        Export();
        using (var ledger = SupabaseLedgerContext.Create())
        {
            SupabaseMigrations.Export(ledger, _directory).Created.Should().ContainSingle();
            SupabaseMigrations.Compare(ledger, _directory).IsInSync.Should().BeTrue();
        }

        Compare().IsInSync.Should().BeTrue();
        File.ReadAllText(Path.Combine(_directory, FileOf(CreateLedger.Id, "supabaseledger"))).Should().Contain("of SupabaseLedgerContext.");
    }

    [Fact]
    public void EnsureInSync_names_each_problem_and_what_to_do()
    {
        Export();
        File.Delete(Path.Combine(_directory, FileOf(AddShelfCapacity.Id)));
        using var context = SupabaseShelfContext.Create();

        var act = () => SupabaseMigrations.EnsureInSync(context, _directory);

        act.Should().Throw<SupabaseMigrationsOutOfSyncException>()
            .WithMessage($"*{AddShelfCapacity.Id}: has no file. A build with SupabaseMigrationsExport=Write*writes {FileOf(AddShelfCapacity.Id)}*")
            .Which.Problems.Should().ContainSingle();
    }

    private static readonly SupabaseMigrationSource Shelves = SupabaseMigrationSource.For(() => SupabaseShelfContext.Create());
    private static readonly SupabaseMigrationSource Ledger = SupabaseMigrationSource.For(() => SupabaseLedgerContext.Create());

    [Fact]
    public void Several_sources_export_into_one_directory_without_a_host()
    {
        var reports = SupabaseMigrations.Export([Shelves, Ledger], _directory);

        reports.Should().HaveCount(2);
        reports.SelectMany(r => r.Created).Select(e => e.MigrationId).Should().Equal(CreateShelves.Id, AddShelfCapacity.Id, CreateLedger.Id);
        SupabaseMigrations.Compare([Shelves, Ledger], _directory).Should().OnlyContain(r => r.IsInSync);
    }

    [Fact]
    public void EnsureInSync_over_several_sources_reports_the_problems_of_all_of_them_in_one_message()
    {
        SupabaseMigrations.Export([Shelves, Ledger], _directory);
        File.Delete(Path.Combine(_directory, FileOf(AddShelfCapacity.Id)));
        File.Delete(Path.Combine(_directory, FileOf(CreateLedger.Id, "supabaseledger")));

        var act = () => SupabaseMigrations.EnsureInSync([Shelves, Ledger], _directory);

        act.Should().Throw<SupabaseMigrationsOutOfSyncException>()
            .WithMessage($"*{AddShelfCapacity.Id}: has no file*{CreateLedger.Id}: has no file*")
            .Which.Reports.Should().HaveCount(2);
    }

    [Fact]
    public void Without_a_directory_the_sources_find_the_supabase_project_from_the_current_directory()
    {
        // The directory argument is left out on purpose; the current directory is a test output folder
        // with no supabase/config.toml above it, so the lookup has to say so.
        var act = () => SupabaseMigrations.Compare([Shelves]);

        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*supabase/config.toml*");
    }

    [Fact]
    public void Registering_the_same_context_twice_registers_it_once()
    {
        var services = new ServiceCollection()
            .AddSupabaseMigrations(Shelves)
            .AddSupabaseMigrations(SupabaseMigrationSource.For(() => SupabaseShelfContext.Create()))
            .AddSupabaseMigrations(Ledger);

        services.BuildServiceProvider().GetSupabaseMigrationSources().Select(s => s.ContextType)
            .Should().Equal(typeof(SupabaseShelfContext), typeof(SupabaseLedgerContext));
    }

    [Fact]
    public void FindDirectory_looks_upwards_for_the_supabase_project_like_the_cli_does()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_directory, "src", "Host", "bin"));
        Directory.CreateDirectory(Path.Combine(_directory, "supabase"));
        File.WriteAllText(Path.Combine(_directory, "supabase", "config.toml"), "project_id = \"test\"\n");

        SupabaseMigrations.FindDirectory(nested.FullName).Should().Be(Path.Combine(_directory, "supabase", "migrations"));
    }

    [Fact]
    public void FindDirectory_says_so_when_there_is_no_supabase_project()
    {
        var nowhere = Directory.CreateDirectory(Path.Combine(_directory, "empty"));

        var act = () => SupabaseMigrations.FindDirectory(nowhere.FullName);

        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*supabase/config.toml*supabase init*");
    }

    [Fact]
    public void Another_provider_is_refused_because_supabase_runs_postgres()
    {
        using var context = new SupabaseShelfContext(new DbContextOptionsBuilder<SupabaseShelfContext>().UseSqlite("Data Source=:memory:").Options);

        var act = () => SupabaseMigrations.Generate(context);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Microsoft.EntityFrameworkCore.Sqlite*UseNpgsql*");
    }

    [Fact]
    public void A_migration_id_without_a_timestamp_is_refused()
    {
        using var context = UntimedMigrationContext.Create();

        var act = () => SupabaseMigrations.Generate(context);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Initial'*yyyyMMddHHmmss*");
    }

    /// <summary>
    /// The files applied the way the Supabase CLI applies them, one implicit transaction per file, against
    /// a real Postgres. Afterwards Entity Framework must agree that nothing is pending, and a Data API role
    /// must see nothing in a table it has been granted.
    /// </summary>
    [Fact]
    public async Task Applied_like_the_cli_applies_them_entity_framework_sees_nothing_pending_and_anon_sees_no_rows()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var database = await PgmqDatabase.StartAsync(cancellation);
        RequiredContainers.EnforceOrSkip(
            available: database is not null,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.Image}' could not be started. Applying exported migrations is not covered on this machine.");

        await using (var connection = await database!.OpenAsync(cancellation))
        {
            foreach (var file in Generate())
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellation);
                await using var command = new NpgsqlCommand(file.Sql, connection, transaction);
                await command.ExecuteNonQueryAsync(cancellation);
                await transaction.CommitAsync(cancellation);
            }
        }

        await using (var context = SupabaseShelfContext.Create(database.ConnectionString))
        {
            (await context.Database.GetAppliedMigrationsAsync(cancellation)).Should().Equal(CreateShelves.Id, AddShelfCapacity.Id);
            (await context.Database.GetPendingMigrationsAsync(cancellation)).Should().BeEmpty();

            context.Shelves.Add(new SupabaseShelf { Id = Guid.CreateVersion7(), Name = "Fiction", Capacity = 12 });
            await context.SaveChangesAsync(cancellation);
            (await context.Shelves.CountAsync(cancellation)).Should().Be(1, "the owner is not subject to row level security");
        }

        await using (var connection = await database.OpenAsync(cancellation))
        {
            await using var command = new NpgsqlCommand(
                """
                CREATE ROLE anon NOLOGIN;
                GRANT SELECT ON "Shelves" TO anon;
                SET ROLE anon;
                SELECT count(*) FROM "Shelves";
                """,
                connection);

            (await command.ExecuteScalarAsync(cancellation)).Should().Be(0L, "row level security is on and there is no policy");
        }

        // The start-up check: satisfied on the migrated database, refusing on an empty one.
        await Applications(database.ConnectionString).EnsureSupabaseMigrationsAppliedAsync(cancellation);

        var empty = await database.CreateDatabaseWithoutPgmqAsync("not_migrated", cancellation);
        var refuse = () => Applications(empty).EnsureSupabaseMigrationsAppliedAsync(cancellation);

        (await refuse.Should().ThrowAsync<SupabaseMigrationsPendingException>())
            .WithMessage($"*SupabaseShelfContext: {CreateShelves.Id}, {AddShelfCapacity.Id}*supabase db push*");
    }

    /// <summary>What a host builds: the module's context in the container, and its source registered.</summary>
    private static ServiceProvider Applications(string connectionString)
        => new ServiceCollection()
            .AddDbContext<SupabaseShelfContext>(options => options.UseNpgsql(connectionString))
            .AddSupabaseMigrations(Shelves)
            .BuildServiceProvider();
}
