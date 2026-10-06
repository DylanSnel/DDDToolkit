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
/// see which context a question was asked on. Asked to, it holds readings started together until all of them
/// have sent a statement, so they really run at the same time.
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<SentCommand> _sent = new();
    private Meeting? _meeting;

    /// <summary>The commands sent since the last <see cref="Reset"/>, in order.</summary>
    public IReadOnlyList<SentCommand> Sent => [.. _sent];

    /// <summary>The text of the commands sent since the last <see cref="Reset"/>, in order.</summary>
    public IReadOnlyList<string> Commands => [.. _sent.Select(command => command.Text)];

    /// <summary>How many commands were sent since the last <see cref="Reset"/>.</summary>
    public int Count => _sent.Count;

    /// <summary>Forgets the commands sent so far.</summary>
    public void Reset() => _sent.Clear();

    /// <summary>
    /// Holds the statements sent from now on until <paramref name="readings"/> of them have been sent, so readings
    /// started together run at the same time. Returns what to ask afterwards.
    /// </summary>
    /// <remarks>
    /// Readings started with <c>Task.WhenAll</c> may still run one after the other: the first answered, and its
    /// context given back to the pool, before the next takes one, which may then be the very same instance. That
    /// proves nothing about sharing a context. Held here, each reading keeps the context it took until the last
    /// has sent its statement too, so readings that each take a context of their own hold different ones. Readings
    /// that share a context never all get here: Entity Framework refuses the second operation on it, or the second
    /// waits for the first, which waits for it, until the meeting gives up.
    /// <para>
    /// It holds what is sent asynchronously, as the access questions send it, and holds once: once the readings met,
    /// or the meeting gave up on them, statements go on as they come.
    /// </para>
    /// </remarks>
    /// <param name="readings">How many readings are to meet, each with its first statement.</param>
    public Meeting HoldUntilTogether(int readings)
    {
        var meeting = new Meeting(readings);
        Volatile.Write(ref _meeting, meeting);
        return meeting;
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await SendingAsync(command, eventData, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await SendingAsync(command, eventData, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await SendingAsync(command, eventData, cancellationToken);
        return result;
    }

    /// <summary>Records a command, and holds it where readings are to meet.</summary>
    private async Task SendingAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        // Recorded first, while the context is the reading's: held, it stays the reading's until the others came.
        Record(command, eventData);
        if (Volatile.Read(ref _meeting) is { } meeting)
        {
            await meeting.ArriveAsync(cancellationToken);
        }
    }

    private void Record(DbCommand command, CommandEventData eventData) => _sent.Enqueue(new SentCommand(command.CommandText, eventData.Context));

    /// <summary>Readings held until each has sent a statement: where those that run side by side meet.</summary>
    /// <param name="expected">How many readings are to meet.</param>
    public sealed class Meeting(int expected)
    {
        /// <summary>
        /// How long the readings that came wait for the rest: far longer than readings that run need, and short
        /// enough that one that never comes fails the test rather than hanging it.
        /// </summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly TaskCompletionSource _together = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _turn = new();
        private int _arrived;
        private bool _over;
        private bool _met;

        /// <summary>Whether every reading sent its statement while the others were held: whether they ran at the same time.</summary>
        public bool Met
        {
            get
            {
                lock (_turn)
                {
                    return _met;
                }
            }
        }

        internal async Task ArriveAsync(CancellationToken cancellationToken)
        {
            // Who arrives, and whether that is the last, is settled in one step: two readings may arrive from two
            // threads at the same instant, and both must not think themselves the last, or neither.
            lock (_turn)
            {
                if (_over)
                {
                    return;
                }

                if (++_arrived == expected)
                {
                    _met = true;
                    _over = true;
                    _together.TrySetResult();
                    return;
                }
            }

            // Held until the last arrives. When it does not, the meeting is over without it: the ones held go on,
            // those that come later are not held again, and the test sees they never met.
            await Task.WhenAny(_together.Task, Task.Delay(Patience, cancellationToken));
            lock (_turn)
            {
                _over = true;
            }

            _together.TrySetResult();
        }
    }
}
