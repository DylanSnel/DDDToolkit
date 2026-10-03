using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Runs a test's code in the middle of one context's unit of work, which is where a racing command commits on
/// the same database: right after a command of that context that matches, before its next command, or just
/// before that context saves, after Tenancy's interceptor. It fires once, and is disarmed before the code runs,
/// so the racing command's own commands pass through it.
/// <para>
/// The racing command runs between two commands of the context, never inside one, and before the context's
/// save opens a transaction: every context of a test shares one SQLite connection.
/// </para>
/// </summary>
public sealed class CommandHook : DbCommandInterceptor
{
    private readonly Lock _gate = new();
    private Armed? _armed;

    public CommandHook() => Saves = new SaveHook(this);

    /// <summary>The part that fires just before a save; the contexts add it after <c>UseTenancy</c>.</summary>
    public SaveChangesInterceptor Saves { get; }

    /// <summary>Whether the armed code ran.</summary>
    public bool Fired { get; private set; }

    /// <summary>Runs <paramref name="race"/> once, after the first command of <paramref name="context"/> that <paramref name="matches"/>, before its next.</summary>
    public void AfterCommand(DbContext context, Func<string, bool> matches, Func<Task> race) => Arm(new Armed(context, matches, race));

    /// <summary>Runs <paramref name="race"/> once, when <paramref name="context"/> saves, before anything is written.</summary>
    public void BeforeSave(DbContext context, Func<Task> race) => Arm(new Armed(context, null, race));

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        RunDue(eventData.Context, beforeSave: false);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await RunDueAsync(eventData.Context, beforeSave: false);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        RunDue(eventData.Context, beforeSave: false);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await RunDueAsync(eventData.Context, beforeSave: false);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        RunDue(eventData.Context, beforeSave: false);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await RunDueAsync(eventData.Context, beforeSave: false);
        return result;
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Executed(command, eventData.Context);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Executed(command, eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Executed(command, eventData.Context);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Executed(command, eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Executed(command, eventData.Context);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Executed(command, eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Arm(Armed armed)
    {
        lock (_gate)
        {
            _armed = armed;
            Fired = false;
        }
    }

    private void Executed(DbCommand command, DbContext? context)
    {
        lock (_gate)
        {
            if (_armed is { After: { } matches } armed && ReferenceEquals(armed.Context, context) && matches(command.CommandText))
            {
                armed.Due = true;
            }
        }
    }

    private void RunDue(DbContext? context, bool beforeSave) => RunDueAsync(context, beforeSave).GetAwaiter().GetResult();

    private async Task RunDueAsync(DbContext? context, bool beforeSave)
    {
        Func<Task>? race = null;
        lock (_gate)
        {
            if (_armed is { } armed && ReferenceEquals(armed.Context, context) && (beforeSave ? armed.After is null : armed.Due))
            {
                _armed = null;
                Fired = true;
                race = armed.Race;
            }
        }

        if (race is not null)
        {
            await race();
        }
    }

    private sealed class Armed(DbContext context, Func<string, bool>? after, Func<Task> race)
    {
        public DbContext Context { get; } = context;

        public Func<string, bool>? After { get; } = after;

        public Func<Task> Race { get; } = race;

        public bool Due { get; set; }
    }

    private sealed class SaveHook(CommandHook hook) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            hook.RunDue(eventData.Context, beforeSave: true);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await hook.RunDueAsync(eventData.Context, beforeSave: true);
            return result;
        }
    }
}
