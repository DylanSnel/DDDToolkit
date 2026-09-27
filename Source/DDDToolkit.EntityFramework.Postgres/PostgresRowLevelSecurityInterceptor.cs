using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Makes Postgres apply row level security to a context's queries. Every time the context opens a
/// connection, this sets the role and the token's claims for the caller <see cref="ICallerAccessor"/>
/// names, the way PostgREST does for each request:
/// <code>
/// SELECT set_config('role', 'authenticated', false), set_config('request.jwt.claims', '{"sub":"…"}', false)
/// </code>
/// after which the caller functions answer for that user, <c>ddd.caller_id()</c> on a Postgres of your
/// own and <c>auth.uid()</c> on Supabase, and every policy on every table the context touches applies.
/// </summary>
/// <remarks>
/// Register it with <c>services.AddPostgresRowLevelSecurity()</c> and add it to a context with
/// <c>options.UsePostgresRowLevelSecurity(provider)</c>.
/// <para>
/// <b>The settings last as long as the connection is open, not as long as a transaction.</b> Entity
/// Framework opens a connection for each query outside a transaction and closes it straight after, so
/// they have to be on the connection rather than in a transaction, or a plain query would run without
/// them. That costs one round trip each time a connection is opened. Npgsql clears them with
/// <c>DISCARD ALL</c> before the pooled connection is used again, which is also why this refuses three
/// ways of connecting that would let them reach somebody else: <c>No Reset On Close</c>, which skips that
/// reset; <c>Multiplexing</c>, which shares one connection between callers at once; and Supabase's
/// transaction pooler on port 6543, which hands each transaction whichever server connection is free.
/// Use the session pooler on port 5432, or connect directly.
/// </para>
/// <para>
/// A connection the context did not open itself, one passed to <c>UseNpgsql(connection)</c> already
/// open, gets no settings: it runs as the role it logged in as. Log in as a role that may do nothing but
/// switch roles and that fails closed.
/// </para>
/// </remarks>
public sealed class PostgresRowLevelSecurityInterceptor : DbConnectionInterceptor
{
    /// <summary>What runs on every connection a context opens. Positional parameters, which Npgsql binds with or without its SQL rewriting.</summary>
    internal const string Statement = "SELECT set_config('role', $1, false), set_config('request.jwt.claims', $2, false)";

    private static readonly ConcurrentDictionary<string, string?> Refusals = new(StringComparer.Ordinal);

    private readonly ICallerAccessor _callers;
    private readonly PostgresRowLevelSecurityOptions _options;
    private readonly string _anonymousClaims;
    private readonly string _systemClaims;

    /// <summary>An interceptor that asks <paramref name="callers"/> who is calling, and gives each kind of caller the role <paramref name="options"/> names.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="callers"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A role in <paramref name="options"/> is empty.</exception>
    public PostgresRowLevelSecurityInterceptor(ICallerAccessor callers, PostgresRowLevelSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _callers = callers;
        _options = options;

        // PostgREST gives a request without a user the claims {"role":"anon"}, so the role and claims
        // functions answer the same here. Background work on the login role has no claims at all.
        _anonymousClaims = RoleClaims(options.AnonymousRole);
        _systemClaims = options.SystemRole is null ? string.Empty : RoleClaims(options.SystemRole);
    }

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        EnsureSettingsStayWithTheCaller(connection);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        EnsureSettingsStayWithTheCaller(connection);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(connection);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Why settings made on a connection with <paramref name="connectionString"/> could reach another
    /// caller, or <see langword="null"/> when they cannot. Worked out once per connection string.
    /// </summary>
    internal static string? RefusalFor(string connectionString)
    {
        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            // Not a connection string this can read; the provider will say what is wrong with it.
            return null;
        }

        const string Preamble = "Row level security sets the caller's role and claims on the connection when a context opens it. ";

        if (IsOn(builder, "No Reset On Close") || IsOn(builder, "NoResetOnClose"))
        {
            return Preamble + "'No Reset On Close' hands the connection back to the pool with them still set, to whoever opens it next. Remove it from the connection string.";
        }

        if (IsOn(builder, "Multiplexing"))
        {
            return Preamble + "Multiplexing sends many callers' commands down one connection at the same time, so one caller's role would be every caller's. Turn Multiplexing off.";
        }

        if (IsTransactionPooler(builder))
        {
            return Preamble + "Supabase's transaction pooler, on port 6543, gives every transaction whichever server connection is free, so a query may run without them, or with somebody else's. Connect through the session pooler, on port 5432, or directly.";
        }

        return null;
    }

    /// <summary>
    /// The claims a user made without a token gets: their id as <c>sub</c> and their role, which is all
    /// the caller functions read. A user of a token gets the token's claims as they were signed.
    /// </summary>
    internal static string ClaimsOf(Caller user)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (user.UserId is { } id)
            {
                writer.WriteString("sub", id.ToString("D", CultureInfo.InvariantCulture));
            }

            if (user.Role is { } role)
            {
                writer.WriteString("role", role);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void EnsureSettingsStayWithTheCaller(DbConnection connection)
    {
        if (Refusals.GetOrAdd(connection.ConnectionString ?? string.Empty, RefusalFor) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var caller = _callers.Current
            ?? throw new InvalidOperationException($"{_callers.GetType().Name} said nobody is calling. An {nameof(ICallerAccessor)} answers {nameof(Caller)}.{nameof(Caller.System)} for background work, not null.");

        var claims = caller.Kind switch
        {
            CallerKind.User => caller.Claims ?? ClaimsOf(caller),
            CallerKind.Anonymous => _anonymousClaims,
            _ => _systemClaims,
        };

        var command = connection.CreateCommand();
        command.CommandText = Statement;
        command.Parameters.Add(Parameter(command, _options.RoleOf(caller)));
        command.Parameters.Add(Parameter(command, claims));
        return command;
    }

    private static DbParameter Parameter(DbCommand command, string value)
    {
        var parameter = command.CreateParameter();
        parameter.DbType = DbType.String;
        parameter.Value = value;
        return parameter;
    }

    private static string RoleClaims(string role) => "{\"role\":" + JsonSerializer.Serialize(role) + "}";

    private static bool IsOn(DbConnectionStringBuilder builder, string key)
        => builder.TryGetValue(key, out var value)
            && Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { } text
            && (bool.TryParse(text, out var on) ? on : text is "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>Supabase's transaction pooler: a <c>*.pooler.supabase.com</c> host on port 6543.</summary>
    private static bool IsTransactionPooler(DbConnectionStringBuilder builder)
    {
        var hosts = (builder.TryGetValue("Host", out var host) || builder.TryGetValue("Server", out host))
            ? Convert.ToString(host, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        var port = builder.TryGetValue("Port", out var configured) ? Convert.ToString(configured, CultureInfo.InvariantCulture)?.Trim() : null;

        foreach (var entry in hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.LastIndexOf(':');
            var name = separator > 0 ? entry[..separator] : entry;
            var entryPort = separator > 0 ? entry[(separator + 1)..] : port;

            if (name.EndsWith(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase) && entryPort == "6543")
            {
                return true;
            }
        }

        return false;
    }
}
