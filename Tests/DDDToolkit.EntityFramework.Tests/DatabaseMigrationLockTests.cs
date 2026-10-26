using System.Text;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Sqlite.Migrations.Internal;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

// The providers' own history repositories are in namespaces of internal API: the tests name them to say which one a
// context gets.
#pragma warning disable EF1001

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Which history repository a context gets, and so which lock its migrations take, without a database: on Npgsql the
/// toolkit's, which takes the lock of the whole database and writes the history's SQL as Npgsql does; Npgsql's own for a
/// context that leaves the lock out; the provider's own elsewhere, whose lock is the database's already. The key is the
/// one the documentation names. What the lock does on a real Postgres is <see cref="DatabaseMigrationLockPostgresTests"/>.
/// </summary>
public sealed class DatabaseMigrationLockTests : IDisposable
{
    private const string Nowhere = "Host=nowhere.invalid;Database=unused";

    private readonly ServiceProvider _services = new ServiceCollection().AddDDDToolkitEntityFramework().BuildServiceProvider();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void On_Npgsql_the_history_takes_the_lock_of_the_whole_database_and_writes_its_SQL_as_Npgsql_does()
    {
        using var locked = Dock(options => options.UseNpgsql(Nowhere).UseDDDToolkit(_services));
        using var npgsqls = Dock(options => options.UseNpgsql(Nowhere).UseDDDToolkit(_services).WithoutDatabaseMigrationLock());

        var repository = locked.GetService<IHistoryRepository>();
        var own = npgsqls.GetService<IHistoryRepository>();

        own.Should().BeOfType<NpgsqlHistoryRepository>("a context that leaves the lock out keeps Npgsql's repository and its lock");
        repository.Should().NotBeOfType<NpgsqlHistoryRepository>("the toolkit's takes the lock of the database before Npgsql's");
        repository.LockReleaseBehavior.Should().Be(LockReleaseBehavior.Transaction, "both locks last the migration's transaction");

        var row = new HistoryRow(CreateDock.Id, "10.0.0");
        repository.GetCreateIfNotExistsScript().Should().Be(own.GetCreateIfNotExistsScript()).And.Contain("dock.\"__EFMigrationsHistory\"");
        repository.GetInsertScript(row).Should().Be(own.GetInsertScript(row));
        repository.GetDeleteScript(CreateDock.Id).Should().Be(own.GetDeleteScript(CreateDock.Id));
        repository.GetBeginIfNotExistsScript(CreateDock.Id).Should().Be(own.GetBeginIfNotExistsScript(CreateDock.Id));
        repository.GetBeginIfExistsScript(CreateDock.Id).Should().Be(own.GetBeginIfExistsScript(CreateDock.Id));
        repository.GetEndIfScript().Should().Be(own.GetEndIfScript());
    }

    [Fact]
    public void Every_call_that_wires_the_toolkit_brings_the_lock_and_a_design_time_factory_leaves_it_out_in_the_same_expression()
    {
        using var core = Dock(options => options.UseNpgsql(Nowhere).UseDDDToolkitCore(_services));
        using var designTime = new DockContext(new DbContextOptionsBuilder<DockContext>().UseNpgsql(Nowhere).UseDDDToolkitDesignTime().Options);
        using var designTimeWithout = new DockContext(new DbContextOptionsBuilder<DockContext>().UseNpgsql(Nowhere).UseDDDToolkitDesignTime().WithoutDatabaseMigrationLock().Options);
        using var calledAgain = Dock(options => options.UseNpgsql(Nowhere).UseDDDToolkit(_services).WithoutDatabaseMigrationLock().UseDDDToolkit(_services));

        core.GetService<IHistoryRepository>().Should().NotBeOfType<NpgsqlHistoryRepository>("the lock is the base's, as the history is");
        designTime.GetService<IHistoryRepository>().Should().NotBeOfType<NpgsqlHistoryRepository>("dotnet ef database update and a bundle migrate with the factory's context");
        designTimeWithout.GetService<IHistoryRepository>().Should().BeOfType<NpgsqlHistoryRepository>();
        calledAgain.GetService<IHistoryRepository>().Should().BeOfType<NpgsqlHistoryRepository>("a toolkit call after it does not bring the lock back");
        HistoryOf(calledAgain).Should().Be("dock.\"__EFMigrationsHistory\"", "leaving the lock out leaves the history where the toolkit keeps it");
    }

    [Fact]
    public void Leaving_the_lock_out_comes_after_the_toolkits_call_and_shares_its_internal_services()
    {
        // Before the toolkit's call there is no lock to leave out, and the call would bring it after all: that is said.
        FluentActions.Invoking(() => new DbContextOptionsBuilder<DockContext>().UseNpgsql(Nowhere).WithoutDatabaseMigrationLock())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("WithoutDatabaseMigrationLock() leaves out the migration lock*Call it after the toolkit's call*");

        // The choice is read as each context migrates, so a context that leaves the lock out is no reason for Entity
        // Framework to build a second set of internal services.
        using var locked = Dock(options => options.UseNpgsql(Nowhere).UseDDDToolkit(_services));
        using var without = Dock(options => options.UseNpgsql(Nowhere).UseDDDToolkit(_services).WithoutDatabaseMigrationLock());
        without.GetService<IHistoryRepository>().Should().BeOfType<NpgsqlHistoryRepository>();
        locked.GetService<IHistoryRepository>().Should().NotBeOfType<NpgsqlHistoryRepository>();
        without.GetService<IModelSource>().Should().BeSameAs(locked.GetService<IModelSource>(), "both contexts run on one set of internal services");
    }

    [Fact]
    public void On_SQLite_the_history_is_the_providers_own_whose_lock_is_the_database_already()
    {
        using var context = Dock(options => options.UseSqlite("Data Source=:memory:").UseDDDToolkit(_services));

        context.GetService<IHistoryRepository>().Should().BeOfType<SqliteHistoryRepository>();
    }

    [Fact]
    public void The_statement_takes_the_documented_key_which_pg_locks_shows_as_two_halves()
    {
        DatabaseMigrationLock.PostgresStatement.Should().Be($"SELECT pg_catalog.pg_advisory_xact_lock({DatabaseMigrationLock.PostgresKey})");
        DatabaseMigrationLock.PostgresKey.Should().Be(4919131821860013906);

        var bytes = BitConverter.GetBytes(DatabaseMigrationLock.PostgresKey);
        Array.Reverse(bytes);
        Encoding.ASCII.GetString(bytes).Should().Be("DDDTMIGR");

        (DatabaseMigrationLock.PostgresKey >> 32).Should().Be(1145324628, "pg_locks shows the high half as classid");
        (DatabaseMigrationLock.PostgresKey & 0xFFFF_FFFF).Should().Be(1296648018, "and the low half as objid");
        DatabaseMigrationLock.PostgresKey.Should().BeGreaterThan(int.MaxValue, "pgmq locks its queues with 32-bit keys, which this one is not");
    }

    /// <summary>The table the context's history repository records a migration in, as its SQL names it.</summary>
    private static string HistoryOf(DbContext context)
    {
        var insert = context.GetService<IHistoryRepository>().GetInsertScript(new HistoryRow(CreateDock.Id, "10.0.0"));
        var start = insert.IndexOf("INSERT INTO ", StringComparison.Ordinal) + "INSERT INTO ".Length;
        return insert[start..insert.IndexOf(" (", start, StringComparison.Ordinal)];
    }

    private static DockContext Dock(Action<DbContextOptionsBuilder<DockContext>> configure)
    {
        var options = new DbContextOptionsBuilder<DockContext>();
        configure(options);
        return new DockContext(options.Options);
    }
}
