using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Examples.Hosting;

/// <summary>
/// How many connections a host may hold on its Postgres, said per purpose: what answers requests, and what runs
/// in the background, the outbox pollers and the rest of the toolkit's bookkeeping. Each purpose gets a data
/// source of its own (<see cref="PostgresPools"/>), so a burst of requests cannot starve the pollers, and a
/// poller that hangs cannot take a connection from a request.
/// </summary>
/// <remarks>
/// A context pool bounds memory, not connections: a context holds a connection only while a command runs or a
/// transaction is open. The bound is the data source's <c>Maximum Pool Size</c>, so that is what is budgeted here.
/// Over every process that connects, <c>instances × (Requests + Background)</c> has to fit in what the database,
/// or the pooler in front of it, allows.
/// </remarks>
/// <param name="Requests">The most connections requests hold at once. At least the widest single request, so one request cannot wait for itself.</param>
/// <param name="Background">The most connections background work holds at once: one per poller is enough.</param>
/// <param name="IdleLifetime">How long a connection nobody uses is kept open.</param>
/// <param name="Lifetime">How long a connection is used before it is replaced, so a database that moved is found again.</param>
/// <param name="Timeout">How long a command waits for a free connection before it fails, rather than hang.</param>
/// <param name="ApplicationName">What the database sees each connection as, with the purpose behind it: <c>tenancy-requests</c>, <c>tenancy-background</c>.</param>
public sealed record PostgresPoolBudget(int Requests, int Background, TimeSpan IdleLifetime, TimeSpan Lifetime, TimeSpan Timeout, string ApplicationName)
{
    /// <summary>What a host gets that configures nothing: sixteen connections for requests and four for the background.</summary>
    public static PostgresPoolBudget Default(string applicationName)
        => new(Requests: 16, Background: 4, IdleLifetime: TimeSpan.FromSeconds(60), Lifetime: TimeSpan.FromMinutes(15), Timeout: TimeSpan.FromSeconds(15), applicationName);

    /// <summary>
    /// The budget <paramref name="section"/> states, each value optional: <c>Requests</c> and <c>Background</c> as
    /// whole numbers, <c>IdleLifetime</c>, <c>Lifetime</c> and <c>Timeout</c> in seconds. What is left out is
    /// <see cref="Default"/>'s.
    /// </summary>
    /// <param name="section">The section, such as <c>Sample:Pools</c>.</param>
    /// <param name="applicationName">What the database sees each connection as, before the purpose.</param>
    /// <exception cref="InvalidOperationException">A value is no whole number, or not above zero.</exception>
    public static PostgresPoolBudget From(IConfiguration section, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var standard = Default(applicationName);
        return standard with
        {
            Requests = Read(section, nameof(Requests)) ?? standard.Requests,
            Background = Read(section, nameof(Background)) ?? standard.Background,
            IdleLifetime = Seconds(section, nameof(IdleLifetime)) ?? standard.IdleLifetime,
            Lifetime = Seconds(section, nameof(Lifetime)) ?? standard.Lifetime,
            Timeout = Seconds(section, nameof(Timeout)) ?? standard.Timeout,
        };
    }

    private static TimeSpan? Seconds(IConfiguration section, string key) => Read(section, key) is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

    private static int? Read(IConfiguration section, string key)
    {
        if (section[key] is not { Length: > 0 } written)
        {
            return null;
        }

        return int.TryParse(written, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"'{key}' of the connection budget is '{written}'. It takes a whole number above zero.");
    }
}
