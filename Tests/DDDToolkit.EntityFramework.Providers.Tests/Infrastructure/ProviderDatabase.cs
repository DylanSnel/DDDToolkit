using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// One throw-away database inside a running container: created by <c>EnsureCreated</c> from the
/// DDDToolkit model and dropped again when the test that owns it ends. A database per test is the
/// only isolation that does not need the tests to know about each other, and it is also the only way
/// to assert on whole-table row counts without a previous test's rows in them.
/// </summary>
/// <param name="configure">Points a builder at this database with this provider.</param>
public sealed class ProviderDatabase(Action<DbContextOptionsBuilder> configure) : IAsyncDisposable
{
    /// <summary>
    /// Points a context options builder at this database. Handed to <c>AddDbContext</c> by the tests
    /// that need a container, so the framework's own registration and these tests talk to the same
    /// database with the same provider.
    /// </summary>
    public Action<DbContextOptionsBuilder> Configure { get; } = configure;

    /// <summary>A context with an empty change tracker, so a read-back comes from the database.</summary>
    public ProviderContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ProviderContext>();
        Configure(builder);
        return new ProviderContext(builder.Options);
    }

    /// <summary>Creates the database and every table the model describes, schemas included.</summary>
    public async Task CreateAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }

    /// <summary>Runs a scalar query on the database's own connection, for assertions about the schema.</summary>
    public async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    /// <summary>
    /// The declared type of one column, read from <c>information_schema</c>. Both providers here
    /// implement it, so the same query answers for both and the difference in the answer is the point.
    /// </summary>
    public async Task<string?> ColumnTypeAsync(string table, string column, CancellationToken cancellationToken)
        => (string?)await ScalarAsync(
            $"SELECT data_type FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'",
            cancellationToken);

    /// <summary>The schema a table was created in, read from <c>information_schema</c>.</summary>
    public async Task<string?> TableSchemaAsync(string table, CancellationToken cancellationToken)
        => (string?)await ScalarAsync(
            $"SELECT table_schema FROM information_schema.tables WHERE table_name = '{table}'",
            cancellationToken);

    /// <summary>
    /// The number of rows in a table. Identifiers are quoted the ANSI way, which SQL Server also
    /// accepts because SqlClient sets QUOTED_IDENTIFIER on.
    /// </summary>
    public async Task<int> CountRowsAsync(string table, string? schema, CancellationToken cancellationToken)
    {
        var qualified = schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
        return Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {qualified}", cancellationToken), CultureInfo.InvariantCulture);
    }

    /// <summary>Drops the database, so the container does not grow one schema per test.</summary>
    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
