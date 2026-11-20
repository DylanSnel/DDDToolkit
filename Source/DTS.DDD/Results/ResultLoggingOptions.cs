using Microsoft.Extensions.Logging;

namespace DTS.DDD.Results;

/// <summary>
/// The log level of a failed result per error type, for the log line the mediator writes when a request answers a
/// failure. The most specific type wins. The defaults: <see cref="UnexpectedError"/> at Error,
/// <see cref="NotPermittedError"/> at Warning, every other error at Information.
/// </summary>
/// <remarks>
/// The line names the request, the error type and the code. It leaves out the message and the arguments, which may hold
/// personal data such as an address. <see cref="LogLevel.None"/> leaves a type out of the log.
/// </remarks>
public sealed class ResultLoggingOptions
{
    private readonly Dictionary<Type, LogLevel> _levels = new()
    {
        [typeof(Error)] = LogLevel.Information,
        [typeof(NotPermittedError)] = LogLevel.Warning,
        [typeof(UnexpectedError)] = LogLevel.Error,
    };

    /// <summary>Logs a failure with an error of <typeparamref name="TError"/>, or derived from it, at <paramref name="level"/>.</summary>
    public ResultLoggingOptions Level<TError>(LogLevel level)
        where TError : Error
    {
        _levels[typeof(TError)] = level;
        return this;
    }

    /// <summary>The level for <paramref name="error"/>: that of its type, or of the nearest base type with a level.</summary>
    public LogLevel LevelOf(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        for (var type = error.GetType(); type is not null; type = type.BaseType)
        {
            if (_levels.TryGetValue(type, out var level))
            {
                return level;
            }
        }

        return LogLevel.Information;
    }
}