using System.Collections.Concurrent;
using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>One command a context sent: its text, and the toolkit's caller and Tenancy's that were current when it ran.</summary>
public sealed record RecordedCommand(string Text, Caller? Caller, ITenancyCaller? TenancyCaller);

/// <summary>
/// Records every command the contexts send, with or without await, with who was calling, so a test can see what a
/// flow of work read and wrote, and as whom. Added after the row level security interceptor, so it sees the
/// statements, not the settings.
/// </summary>
public sealed class CommandRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<RecordedCommand> _sent = new();

    /// <summary>The commands sent so far, in order.</summary>
    public IReadOnlyList<RecordedCommand> Sent => [.. _sent];

    /// <summary>Forgets the commands sent so far.</summary>
    public void Clear() => _sent.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return result;
    }

    private void Record(DbCommand command) => _sent.Enqueue(new RecordedCommand(command.CommandText, Callers.Ambient, TenancyCallers.Ambient));
}
