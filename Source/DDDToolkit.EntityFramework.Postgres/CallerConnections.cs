using System.Data.Common;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Connections for SQL a module sends outside Entity Framework, a report read with a data reader or a bulk
/// copy, carrying the current caller exactly as a context's connections do: the role, the claims, every
/// <see cref="IRowLevelSecuritySettings"/> and the statement timeout, set with the interceptor's own statement
/// and refused by the interceptor's own rules. Register it with
/// <see cref="DependencyInjection.AddCallerConnections"/>.
/// <code>
/// await using var connection = await connections.OpenAsync(cancellationToken);
/// await using var command = connection.CreateCommand();
/// command.CommandText = "SELECT count(*) FROM ordering.\"Orders\"";   // the caller's orders, by the policies
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>One caller per connection.</b> What is set is the caller there is when the connection is opened, or the
/// transaction begun. Dispose the connection before the caller changes; nothing here follows a caller that
/// changes afterwards, as a context does before each of its commands.
/// </para>
/// <para>
/// What it sets is remembered where the interceptor remembers it, so a context handed such a connection, with
/// <c>UseNpgsql(connection)</c>, or such a transaction, with <c>Database.UseTransaction</c>, finds the caller
/// there and sends nothing of its own.
/// </para>
/// <para>
/// Without it, SQL on <c>context.Database.GetDbConnection()</c> runs as whatever that connection carries at
/// that moment: the caller while a context holds it open where the settings last a session, and the role the
/// application logged in as where they last one transaction, since nothing lives on a session there.
/// </para>
/// </remarks>
public sealed class CallerConnections
{
    private readonly DbDataSource _dataSource;
    private readonly PostgresRowLevelSecurityInterceptor _interceptor;

    /// <summary>Connections from <paramref name="dataSource"/>, with the caller set as <paramref name="interceptor"/> sets it.</summary>
    /// <param name="dataSource">Where the connections come from, such as the data source the host's contexts use.</param>
    /// <param name="interceptor">The interceptor the host's contexts use: its caller accessor, its roles, its settings and its scope.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public CallerConnections(DbDataSource dataSource, PostgresRowLevelSecurityInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(interceptor);

        _dataSource = dataSource;
        _interceptor = interceptor;
    }

    /// <summary>
    /// An open connection with the caller set for its session. Dispose it when the work is done: the reset a
    /// pooled connection gets before it is used again takes the caller off.
    /// </summary>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <exception cref="InvalidOperationException">
    /// The settings last one transaction (<see cref="RowLevelSecurityScope.Transaction"/>), where nothing may
    /// live on a session: use <see cref="BeginTransactionAsync"/>. Or the data source's connection string would
    /// hand the settings on to another caller, or the caller has no role to run as.
    /// </exception>
    /// <exception cref="NoCallerException">Nobody is calling, in a host that requires explicit callers.</exception>
    /// <exception cref="RefusalException">The caller's token carries a role that is on no list.</exception>
    public async ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_interceptor.Scope == RowLevelSecurityScope.Transaction)
        {
            throw new InvalidOperationException(
                $"The caller's settings last one transaction here ({nameof(RowLevelSecurityScope)}.{nameof(RowLevelSecurityScope.Transaction)}), so a connection cannot carry a caller for its session: " +
                $"whoever is handed the server connection next would find it. Use {nameof(BeginTransactionAsync)}, which sets the caller for one transaction.");
        }

        var connection = _dataSource.CreateConnection();
        try
        {
            _interceptor.EnsureConnects(connection);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await _interceptor.ApplyAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            // Never handed out half set: a connection the caller could not be set on would run as the login role.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// An open connection with a transaction begun on it, and the caller set for that transaction alone, as its
    /// first statement. It works in both scopes. The connection is the transaction's
    /// (<see cref="DbTransaction.Connection"/>) and is yours to dispose, with the transaction:
    /// <code>
    /// await using var transaction = await connections.BeginTransactionAsync(cancellationToken);
    /// await using var connection = transaction.Connection!;
    /// </code>
    /// </summary>
    /// <param name="cancellationToken">Cancels the opening and the first statement.</param>
    /// <exception cref="InvalidOperationException">The data source's connection string is refused in this scope, or the caller has no role to run as.</exception>
    /// <exception cref="NoCallerException">Nobody is calling, in a host that requires explicit callers.</exception>
    /// <exception cref="RefusalException">The caller's token carries a role that is on no list.</exception>
    public async ValueTask<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var connection = _dataSource.CreateConnection();
        try
        {
            _interceptor.EnsureConnects(connection);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await _interceptor.ApplyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            // Closing the connection ends the transaction with it.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
