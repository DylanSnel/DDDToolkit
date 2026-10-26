using System.Collections.Concurrent;
using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// One command a context sent: its text, the transaction it ran in, if any, the context, the connection it ran on,
/// the toolkit's caller that was current when it ran, whether an ambient <c>TransactionScope</c> was, and Tenancy's
/// caller that was.
/// </summary>
public sealed record SentCommand(
    string Text,
    DbTransaction? Transaction,
    DbContext? Context,
    DbConnection? Connection,
    Caller? Caller,
    bool InAmbientTransaction = false,
    ITenancyCaller? TenancyCaller = null)
{
    /// <summary>Whether it changes rows rather than reading them.</summary>
    public bool Writes => Text.Contains("INSERT ", StringComparison.Ordinal)
                          || Text.Contains("UPDATE ", StringComparison.Ordinal)
                          || Text.Contains("DELETE ", StringComparison.Ordinal);
}

/// <summary>
/// Records every command a context sends, so a test can count round trips, read the SQL a query became, and see
/// which transaction a write ran in.
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<SentCommand> _sent = new();

    /// <summary>The commands sent since the last <see cref="Reset"/>, in order.</summary>
    public IReadOnlyList<SentCommand> Sent => [.. _sent];

    /// <summary>The text of the commands sent since the last <see cref="Reset"/>, in order.</summary>
    public IReadOnlyList<string> Commands => [.. _sent.Select(command => command.Text)];

    /// <summary>How many commands were sent since the last <see cref="Reset"/>.</summary>
    public int Count => _sent.Count;

    /// <summary>Forgets the commands sent so far.</summary>
    public void Reset() => _sent.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    private void Record(DbCommand command, CommandEventData eventData)
        => _sent.Enqueue(new SentCommand(
            command.CommandText,
            command.Transaction,
            eventData.Context,
            command.Connection,
            Callers.Ambient,
            System.Transactions.Transaction.Current is not null,
            TenancyCallers.Ambient));
}
