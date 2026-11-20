using System.Reflection;

namespace DTS.DDD.Results;

/// <summary>
/// Whether <typeparamref name="TResponse"/> is <see cref="Result"/> or a <see cref="Result{T}"/>, and how to make a
/// failure of it. The mediator reads it once per response type.
/// </summary>
/// <typeparam name="TResponse">The response of a request.</typeparam>
public static class ResultResponse<TResponse>
{
    private static readonly Func<Error, TResponse>? Failing = MakeFailing();

    private static readonly Func<TResponse, Error?>? Reading = MakeReading();

    /// <summary>Whether a request with this response answers a refusal with a failed result.</summary>
    public static bool IsResult => Failing is not null;

    /// <summary>A failed response with <paramref name="error"/>.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TResponse"/> is no result.</exception>
    public static TResponse Fail(Error error)
        => Failing is { } failing
            ? failing(error)
            : throw new InvalidOperationException($"{typeof(TResponse).Name} is no Result, so it has no failure.");

    /// <summary>The error of a failed <paramref name="response"/>, or null after a success. Reads the struct without boxing it.</summary>
    public static Error? ErrorOf(TResponse response) => Reading is { } reading ? reading(response) : null;

    private static Func<TResponse, Error?>? MakeReading()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (Func<TResponse, Error?>)(object)new Func<Result, Error?>(static result => result.ErrorOrNull);
        }

        if (!typeof(TResponse).IsGenericType || typeof(TResponse).GetGenericTypeDefinition() != typeof(Result<>))
        {
            return null;
        }

        // Once per response type: the typed reader of the closed Result<T>, so the struct is passed as itself.
        var reader = typeof(TResponse).GetMethod(nameof(Result<object>.ErrorOrNullOf), BindingFlags.NonPublic | BindingFlags.Static)!;
        return reader.CreateDelegate<Func<TResponse, Error?>>();
    }

    private static Func<Error, TResponse>? MakeFailing()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return static error => (TResponse)(object)Result.Failure(error);
        }

        if (!typeof(TResponse).IsGenericType || typeof(TResponse).GetGenericTypeDefinition() != typeof(Result<>))
        {
            return null;
        }

        // Once per response type: the static Failure of the closed Result<T>.
        var failure = typeof(TResponse).GetMethod(nameof(Result<object>.Failure), BindingFlags.Public | BindingFlags.Static)!;
        return failure.CreateDelegate<Func<Error, TResponse>>();
    }
}