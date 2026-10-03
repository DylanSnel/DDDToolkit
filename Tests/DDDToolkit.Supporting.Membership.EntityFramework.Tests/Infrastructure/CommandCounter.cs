using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>One command a context sent: its text, and the context that sent it.</summary>
/// <param name="Text">The SQL.</param>
/// <param name="Context">The context it was sent through.</param>
public sealed record SentCommand(string Text, DbContext? Context);

/// <summary>
/// Records every command a context sends, so a test can count round trips, read the SQL a question became, and
/// see which context a question was asked on.
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

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    private void Record(DbCommand command, CommandEventData eventData) => _sent.Enqueue(new SentCommand(command.CommandText, eventData.Context));
}
