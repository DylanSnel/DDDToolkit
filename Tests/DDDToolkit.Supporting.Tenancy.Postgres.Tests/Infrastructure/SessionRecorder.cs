using System.Collections.Concurrent;
using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>What the database's session said of who was asking when a context sent a query: the role it ran as, the tenant setting, and the scope in its claims.</summary>
public sealed record RecordedSession(string Text, string Role, string Tenant, string? Scope);

/// <summary>
/// Asks the database, on the connection of every query a context sends and just before it, who the session says is
/// asking: what the policies and the functions will read, rather than what the application meant to send. Added
/// after the row level security interceptor, so the caller is set when it asks. A query sent without await is
/// asked about the same way.
/// </summary>
public sealed class SessionRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<RecordedSession> _seen = new();

    /// <summary>The sessions of the queries sent so far, in order.</summary>
    public IReadOnlyList<RecordedSession> Seen => [.. _seen];

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await using var asked = Asking(command);
        await using (var reader = await asked.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            Seeing(command, reader);
        }

        return result;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        using var asked = Asking(command);
        using (var reader = asked.ExecuteReader())
        {
            reader.Read();
            Seeing(command, reader);
        }

        return result;
    }

    private static DbCommand Asking(DbCommand command)
    {
        var asked = command.Connection!.CreateCommand();
        asked.Transaction = command.Transaction;
        asked.CommandText =
            $"SELECT CURRENT_USER::text, coalesce(current_setting('{TenancyRowLevelSecurity.TenantSetting}', true), ''), " +
            "nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'scope'";
        return asked;
    }

    private void Seeing(DbCommand command, DbDataReader reader)
        => _seen.Enqueue(new RecordedSession(command.CommandText.Trim(), reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
}

/// <summary>
/// Says who is calling as the flow of work began it, as a host that requires explicit callers does, and remembers
/// every answer it gave with the part of a test it gave it in: who every connection of that part ran as.
/// </summary>
public sealed class ObservedCallers : ICallerAccessor
{
    private readonly ConcurrentQueue<(string During, Caller Caller)> _answered = new();

    /// <summary>The part of the test that is running, by a name the test gives it.</summary>
    public string During { get; set; } = string.Empty;

    /// <summary>Every caller answered so far, with the part it was answered in.</summary>
    public IReadOnlyList<(string During, Caller Caller)> Answered => [.. _answered];

    /// <inheritdoc />
    public Caller Current
    {
        get
        {
            var caller = Callers.Ambient ?? throw new NoCallerException();
            _answered.Enqueue((During, caller));
            return caller;
        }
    }
}
