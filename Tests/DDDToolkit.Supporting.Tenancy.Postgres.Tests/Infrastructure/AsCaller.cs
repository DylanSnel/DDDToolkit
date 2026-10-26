using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using DDDToolkit.EntityFramework.Postgres;
using Npgsql;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// A query as a caller: a raw connection as the login role, in a transaction, with the role, the claims and the
/// tenant setting set for that transaction alone, as the interceptor sets them for a caller. What runs here goes
/// past the use cases, as the application's own SQL that forgot a condition would: only the policies and the
/// triggers stand in its way. Disposing it without <see cref="CommitAsync"/> rolls it back.
/// </summary>
/// <remarks>
/// A statement is written in the default names, and sent in the names of the database it runs on
/// (<see cref="TenancyNaming.Sql"/>), so one test reads the same whatever the tables are called.
/// </remarks>
public sealed class AsCaller : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly TenancyNaming _names;

    private AsCaller(NpgsqlConnection connection, NpgsqlTransaction transaction, TenancyNaming names)
    {
        _connection = connection;
        _transaction = transaction;
        _names = names;
    }

    /// <summary>A signed-in person: <c>authenticated</c>, with their identity as the token's subject, in a tenant.</summary>
    public static Task<AsCaller> PersonAsync(TestDatabase database, Guid identity, TenantId? tenant, CancellationToken cancellationToken)
        => BeginAsync(
            database,
            PostgresRowLevelSecurityOptions.AuthenticatedRole,
            JsonSerializer.Serialize(new { sub = identity.ToString("D", CultureInfo.InvariantCulture), role = PostgresRowLevelSecurityOptions.AuthenticatedRole }),
            tenant,
            cancellationToken);

    /// <summary>
    /// A signed-in person whose token carries <paramref name="tokenRole"/>, which the host mapped to
    /// <paramref name="role"/>: that role, the token's own claims, and the tenant the application would name.
    /// </summary>
    public static Task<AsCaller> TokenRoleAsync(TestDatabase database, string role, string tokenRole, Guid identity, TenantId? tenant, CancellationToken cancellationToken)
        => BeginAsync(
            database,
            role,
            JsonSerializer.Serialize(new { sub = identity.ToString("D", CultureInfo.InvariantCulture), role = tokenRole }),
            tenant,
            cancellationToken);

    /// <summary>System work in a tenant, or in none, in a scope: the scoped system role, with the scope in its claims.</summary>
    public static Task<AsCaller> SystemInAsync(TestDatabase database, TenantId? tenant, string scope, CancellationToken cancellationToken)
        => BeginAsync(
            database,
            PostgresRowLevelSecurityOptions.DefaultSystemInRole,
            JsonSerializer.Serialize(new { role = PostgresRowLevelSecurityOptions.DefaultSystemInRole, scope }),
            tenant,
            cancellationToken);

    /// <summary>The login role itself, which owns the tables: past every policy, though not past the triggers.</summary>
    public static async Task<AsCaller> OwnerAsync(TestDatabase database, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return new AsCaller(connection, await connection.BeginTransactionAsync(cancellationToken), database.Names);
    }

    /// <summary>A request without a user: <c>anon</c>.</summary>
    public static Task<AsCaller> AnonymousAsync(TestDatabase database, CancellationToken cancellationToken)
        => BeginAsync(
            database,
            PostgresRowLevelSecurityOptions.AnonRole,
            JsonSerializer.Serialize(new { role = PostgresRowLevelSecurityOptions.AnonRole }),
            tenant: null,
            cancellationToken);

    /// <summary>Runs <paramref name="sql"/>, and says how many rows it affected.</summary>
    public async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken, params object[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="sql"/> in a savepoint of its own, and says how many rows it affected: when Postgres refuses it,
    /// the transaction goes back to the savepoint and stays usable, where a failed statement would otherwise end it.
    /// </summary>
    public async Task<int> AttemptAsync(string sql, CancellationToken cancellationToken, params object[] parameters)
    {
        await _transaction.SaveAsync("tried", cancellationToken);
        try
        {
            var affected = await ExecuteAsync(sql, cancellationToken, parameters);
            await _transaction.ReleaseAsync("tried", cancellationToken);
            return affected;
        }
        catch (PostgresException)
        {
            await _transaction.RollbackAsync("tried", cancellationToken);
            throw;
        }
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers.</summary>
    public async Task<List<T>> ListAsync<T>(string sql, CancellationToken cancellationToken, params object[] parameters)
    {
        await using var command = Command(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.IsDBNull(0) ? default! : reader.GetFieldValue<T>(0));
        }

        return rows;
    }

    /// <summary>The one value <paramref name="sql"/> answers.</summary>
    public async Task<T> ScalarAsync<T>(string sql, CancellationToken cancellationToken, params object[] parameters)
        => (await ListAsync<T>(sql, cancellationToken, parameters)).Should().ContainSingle().Subject;

    /// <summary>
    /// The plan Postgres makes for <paramref name="query"/> as this caller, with the output of every node named
    /// (<c>EXPLAIN (VERBOSE, FORMAT JSON)</c>): the top node, as <see cref="QueryPlans"/> reads it.
    /// </summary>
    public async Task<JsonElement> PlanAsync(string query, CancellationToken cancellationToken)
    {
        var json = await ScalarAsync<string>("EXPLAIN (VERBOSE, FORMAT JSON) " + query, cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement[0].GetProperty("Plan").Clone();
    }

    /// <summary>
    /// The plan Postgres makes as this caller for a command another connection would send, with its parameters: the
    /// statement Entity Framework writes for a query, as <c>CreateDbCommand</c> gives it.
    /// </summary>
    public async Task<JsonElement> PlanAsync(DbCommand asked, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("EXPLAIN (VERBOSE, FORMAT JSON) " + asked.CommandText, _connection, _transaction);
        foreach (NpgsqlParameter parameter in asked.Parameters)
        {
            command.Parameters.Add(parameter.Clone());
        }

        using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(cancellationToken))!);
        return document.RootElement[0].GetProperty("Plan").Clone();
    }

    /// <summary>Commits, which is when the deferred triggers check what the transaction wrote.</summary>
    public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static async Task<AsCaller> BeginAsync(TestDatabase database, string role, string claims, TenantId? tenant, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var session = new AsCaller(connection, transaction, database.Names);
        await session.ExecuteAsync(
            "SELECT set_config('role', $1, true), set_config('request.jwt.claims', $2, true), set_config($3, $4, true)",
            cancellationToken,
            role,
            claims,
            TenancyRowLevelSecurity.TenantSetting,
            tenant is { } id ? id.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
        return session;
    }

    private NpgsqlCommand Command(string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(_names.Sql(sql), _connection, _transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return command;
    }
}
