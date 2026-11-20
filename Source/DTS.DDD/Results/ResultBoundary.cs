using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTS.DDD.Results;

/// <summary>
/// The edge of the mediator for a request whose response is a <see cref="Result"/> or <see cref="Result{T}"/>: a
/// refusal that a behavior or the handler throws becomes a failed result, and every failure is logged at the level of
/// <see cref="ResultLoggingOptions"/>.
/// </summary>
internal static partial class ResultBoundary
{
    private const string Category = "DTS.DDD.Results";

    private static readonly ResultLoggingOptions DefaultLevels = new();

    public static async ValueTask<TResponse> RunAsync<TMessage, TResponse>(IServiceProvider provider, Func<ValueTask<TResponse>> send)
    {
        TResponse response;
        try
        {
            response = await send().ConfigureAwait(false);
        }
        catch (Exception exception) when (Errors.FromException(exception) is { } error)
        {
            // A refusal of an access check, of a manager or of a value object: thrown below, answered as a result.
            Log(provider, typeof(TMessage), error);
            return ResultResponse<TResponse>.Fail(error);
        }

        if (ResultResponse<TResponse>.ErrorOf(response) is { } returned)
        {
            Log(provider, typeof(TMessage), returned);
        }

        return response;
    }

    // Only a failure gets here. The level is read first, and nothing is formatted for a level the logger leaves out.
    private static void Log(IServiceProvider provider, Type message, Error error)
    {
        var level = (provider.GetService<ResultLoggingOptions>() ?? DefaultLevels).LevelOf(error);
        if (level == LogLevel.None || provider.GetService<ILoggerFactory>()?.CreateLogger(Category) is not { } logger || !logger.IsEnabled(level))
        {
            return;
        }

        LogFailure(logger, level, message.Name, error.GetType().Name, error.Code);
    }

    [LoggerMessage(EventId = 1, Message = "{Request} gave {ErrorType} {Code}")]
    private static partial void LogFailure(ILogger logger, LogLevel level, string request, string errorType, string code);
}