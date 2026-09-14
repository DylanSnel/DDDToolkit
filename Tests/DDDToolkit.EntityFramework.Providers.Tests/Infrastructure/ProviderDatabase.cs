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

    /// <summary>
    /// A context that maps only the outbox and the inbox, under names of their own, so a test can put
    /// a second timestamp shape in the same database and compare the two tables side by side.
    /// </summary>
    public TimestampShapeContext CreateTimestampShapeContext()
    {
        var builder = new DbContextOptionsBuilder<TimestampShapeContext>();
        Configure(builder);
        return new TimestampShapeContext(builder.Options);
    }

    /// <summary>The default mapping over the table <see cref="TimestampShapeContext"/> writes.</summary>
    public UpgradedTimestampContext CreateUpgradedTimestampContext()
    {
        var builder = new DbContextOptionsBuilder<UpgradedTimestampContext>();
        Configure(builder);
        return new UpgradedTimestampContext(builder.Options);
    }

    /// <summary>
    /// Runs a script on the database's own connection, one batch at a time.
    /// <para>
    /// <c>GenerateCreateScript</c> on SQL Server separates its statements with <c>GO</c>, which is not
    /// SQL at all: it is a word the command line tools understand and the server does not. So the
    /// script is split on it here, which is what those tools do too. Every other provider emits one
    /// batch and the split finds nothing.
    /// </para>
    /// </summary>
    public async Task ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in SplitBatches(script))
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>The script's batches: the parts between the lines that say only <c>GO</c>.</summary>
    private static List<string> SplitBatches(string script)
    {
        List<string> batches = [];
        List<string> current = [];

        foreach (var line in script.Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                Take();
            }
            else
            {
                current.Add(line);
            }
        }

        Take();
        return batches;

        void Take()
        {
            var batch = string.Join('\n', current).Trim();
            current.Clear();

            if (batch.Length > 0)
            {
                batches.Add(batch);
            }
        }
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
