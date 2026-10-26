using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// The pallet depot's requests, for the tests of what a refused save logs: a keeper of a depot may retitle and add
// its pallets, whoever owns them. The access check asks who keeps which depot of whatever the test says keeps
// that list, so a test decides whether C# and the database read the same keepers, and when one of them changes.

/// <summary>What every request of the depot implements.</summary>
public interface IDepotRequest : IRequireAccess;

/// <summary>The caller keeps the depot.</summary>
/// <param name="Depot">The depot, from the request.</param>
public sealed record KeepsDepot(DepotId Depot) : AccessRequirement;

/// <summary>Retitles a pallet of a depot the caller keeps.</summary>
public sealed record RetitlePallet(PalletId Pallet, DepotId Depot, string Label) : IDepotRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new KeepsDepot(Depot);
}

/// <summary>Adds a pallet to a depot the caller keeps, in anyone's name.</summary>
public sealed record AddPallet(DepotId Depot, int Number, string Label, Guid Owner) : IDepotRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new KeepsDepot(Depot);
}

/// <summary>Looks at a depot: anyone may.</summary>
public sealed record LookAtDepot(DepotId Depot) : IDepotRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
}

/// <summary>Who keeps which depot, as the access check reads it.</summary>
public interface IDepotKeepers
{
    Task<bool> KeepsAsync(Guid user, DepotId depot, CancellationToken cancellationToken);
}

/// <summary>Keepers C# alone knows of, changed by a test whenever it likes.</summary>
public sealed class KeepersInMemory : IDepotKeepers
{
    private readonly HashSet<(Guid User, DepotId Depot)> _keepers = [];

    /// <summary>How many times the check asked.</summary>
    public int Asked { get; private set; }

    /// <summary>What every asking after the first throws, in place of an answer.</summary>
    public Exception? FailsAfterTheFirst { get; set; }

    public KeepersInMemory Keeps(Guid user, DepotId depot)
    {
        _keepers.Add((user, depot));
        return this;
    }

    public void StopsKeeping(Guid user, DepotId depot) => _keepers.Remove((user, depot));

    public async Task<bool> KeepsAsync(Guid user, DepotId depot, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (++Asked > 1 && FailsAfterTheFirst is { } failure)
        {
            throw failure;
        }

        return _keepers.Contains((user, depot));
    }
}

/// <summary>Decides <see cref="KeepsDepot"/> for the caller of the test.</summary>
public sealed class KeeperCheck(Caller caller, IDepotKeepers keepers) : IAccessCheck
{
    /// <summary>The code a caller who does not keep the depot is refused with.</summary>
    public const string NotAKeeper = "depots.not-a-keeper";

    public bool Decides(AccessRequirement requirement) => requirement is KeepsDepot;

    public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        if (caller.UserId is not { } user || !await keepers.KeepsAsync(user, ((KeepsDepot)requirement).Depot, cancellationToken))
        {
            throw new RefusalException(NotAKeeper, RefusalKind.NotPermitted, "Only a keeper of the depot does that.");
        }
    }

    /// <summary>
    /// Sends <paramref name="request"/> as a pipeline behavior does: the checks, then the handler, in one flow, which
    /// is the flow a save of the handler's runs in.
    /// </summary>
    public static async Task SendAsync(AccessChecks<IDepotRequest> checks, IDepotRequest request, Func<Task> handler, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(request, cancellationToken);
        await handler();
    }
}

/// <summary>Keeps what every logger of a context writes, information and above, by category.</summary>
public sealed class KeptLogLines : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message, Exception? Exception)> _entries = [];

    /// <summary>What the loggers of <typeparamref name="T"/>'s category wrote.</summary>
    public IReadOnlyList<(string Category, LogLevel Level, string Message, Exception? Exception)> Of<T>() => Of(typeof(T).FullName!);

    /// <summary>What the loggers of <paramref name="category"/> wrote: the category of a class the test cannot name, an internal one of the toolkit's.</summary>
    public IReadOnlyList<(string Category, LogLevel Level, string Message, Exception? Exception)> Of(string category)
    {
        lock (_entries)
        {
            return [.. _entries.Where(entry => entry.Category == category)];
        }
    }

    public void Clear()
    {
        lock (_entries)
        {
            _entries.Clear();
        }
    }

    /// <summary>A factory that hands out these loggers, writing from <paramref name="level"/> on.</summary>
    public ILoggerFactory Factory(LogLevel level = LogLevel.Information)
        => LoggerFactory.Create(logging => logging.AddProvider(this).SetMinimumLevel(level));

    public ILogger CreateLogger(string categoryName) => new Keeper(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Keeper(KeptLogLines logs, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (logs._entries)
            {
                logs._entries.Add((category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}
