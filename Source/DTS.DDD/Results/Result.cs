namespace DTS.DDD.Results;

/// <summary>
/// The outcome of work that answers no value: success, or an <see cref="Error"/>. A command without an answer returns
/// it, such as <c>ICommand&lt;Result&gt;</c>.
/// </summary>
/// <remarks>
/// A struct, so a success allocates nothing. <c>default(Result)</c> is a success. An <see cref="Error"/> converts to a
/// failed result, so a handler writes <c>return error;</c>.
/// </remarks>
public readonly struct Result : IResultState, IEquatable<Result>
{
    private readonly Error? _error;

    private Result(Error error) => _error = error;

    /// <summary>A success.</summary>
    public static Result Success() => default;

    /// <summary>A failure with <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>A failure with <paramref name="error"/>.</summary>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Whether the work succeeded.</summary>
    public bool IsSuccess => _error is null;

    /// <summary>Whether the work failed.</summary>
    public bool IsFailure => _error is not null;

    /// <summary>Why the work failed.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public Error Error => _error ?? throw new InvalidOperationException("A successful Result has no error.");

    Error? IResultState.ErrorOrNull => _error;

    /// <summary>The error of a failure, or null after a success.</summary>
    internal Error? ErrorOrNull => _error;

    object? IResultState.ValueOrNull => null;

    /// <summary>Runs <paramref name="next"/> after a success, and passes a failure on.</summary>
    public Result Bind(Func<Result> next) => IsSuccess ? next() : this;

    /// <summary>Runs <paramref name="next"/> after a success, and passes a failure on.</summary>
    public Result<T> Bind<T>(Func<Result<T>> next) => IsSuccess ? next() : Result<T>.Failure(_error!);

    /// <summary>A success with the value of <paramref name="map"/>, or this failure.</summary>
    public Result<T> Map<T>(Func<T> map) => IsSuccess ? map() : Result<T>.Failure(_error!);

    /// <summary>This result, or the error of <paramref name="error"/> when <paramref name="predicate"/> is false after a success.</summary>
    public Result Ensure(Func<bool> predicate, Func<Error> error) => IsSuccess && !predicate() ? error() : this;

    /// <summary>Runs <paramref name="action"/> after a success and returns this result.</summary>
    public Result Tap(Action action)
    {
        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Runs <paramref name="action"/> with the error after a failure and returns this result.</summary>
    public Result TapError(Action<Error> action)
    {
        if (_error is { } error)
        {
            action(error);
        }

        return this;
    }

    /// <summary>The answer of <paramref name="onSuccess"/> or of <paramref name="onFailure"/>.</summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure) => _error is { } error ? onFailure(error) : onSuccess();

    /// <summary>Throws an <see cref="ErrorException"/> for a failure.</summary>
    public void ThrowIfFailure()
    {
        if (_error is { } error)
        {
            throw error.ToException();
        }
    }

    /// <inheritdoc />
    public bool Equals(Result other) => Equals(_error, other._error);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _error?.GetHashCode() ?? 0;

    /// <inheritdoc />
    public override string ToString() => _error is { } error ? $"Failure({error})" : "Success";

    public static bool operator ==(Result left, Result right) => left.Equals(right);

    public static bool operator !=(Result left, Result right) => !left.Equals(right);
}