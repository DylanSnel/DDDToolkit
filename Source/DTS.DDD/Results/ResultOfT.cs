namespace DTS.DDD.Results;

/// <summary>
/// The outcome of work that answers a value: the value, or an <see cref="Error"/>. A command or query returns it,
/// such as <c>ICommand&lt;Result&lt;Tenant&gt;&gt;</c>.
/// </summary>
/// <remarks>
/// A struct, so a success allocates nothing. A value and an <see cref="Error"/> both convert to a result, so a handler
/// writes <c>return tenant;</c> or <c>return error;</c>. <c>default(Result&lt;T&gt;)</c> is neither: it is a failure
/// with the code <see cref="DefaultCode"/>, so a result that was never set is never taken for a success.
/// </remarks>
public readonly struct Result<T> : IResultState, IEquatable<Result<T>>
{
    /// <summary>The code of the error of <c>default(Result&lt;T&gt;)</c>.</summary>
    public const string DefaultCode = "ddd.result-not-set";

    private static readonly UnexpectedError NotSet = new(DefaultCode, $"A Result<{typeof(T).Name}> was never set.");

    private readonly T _value;
    private readonly Error? _error;
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _value = value;
        _error = null;
        _isSuccess = true;
    }

    private Result(Error error)
    {
        _value = default!;
        _error = error;
        _isSuccess = false;
    }

    /// <summary>A success with <paramref name="value"/>.</summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>A failure with <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static Result<T> Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>A success with <paramref name="value"/>.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>A failure with <paramref name="error"/>.</summary>
    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>A result without its value: the same success or failure.</summary>
    public static implicit operator Result(Result<T> result) => result._isSuccess ? Result.Success() : Result.Failure(result.Error);

    /// <summary>Whether the work succeeded.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>Whether the work failed.</summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>The value.</summary>
    /// <exception cref="ErrorException">The result is a failure. The exception carries the error.</exception>
    public T Value => _isSuccess ? _value : throw Error.ToException();

    /// <summary>Why the work failed.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public Error Error => _isSuccess ? throw new InvalidOperationException("A successful Result has no error.") : _error ?? NotSet;

    Error? IResultState.ErrorOrNull => _isSuccess ? null : Error;

    // For ResultResponse, which reads a response of this type without boxing it.
    internal static Error? ErrorOrNullOf(Result<T> result) => result._isSuccess ? null : result.Error;

    object? IResultState.ValueOrNull => _isSuccess ? _value : null;

    /// <summary>The value, or <paramref name="fallback"/> after a failure.</summary>
    public T GetValueOrDefault(T fallback) => _isSuccess ? _value : fallback;

    /// <summary>Whether the work succeeded, with the value.</summary>
    public bool TryGetValue(out T value)
    {
        value = _value;
        return _isSuccess;
    }

    /// <summary>A success with the value of <paramref name="map"/>, or this failure.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) => _isSuccess ? map(_value) : Result<TOut>.Failure(Error);

    /// <summary>The result of <paramref name="next"/> after a success, or this failure.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> next) => _isSuccess ? next(_value) : Result<TOut>.Failure(Error);

    /// <summary>The result of <paramref name="next"/> after a success, or this failure.</summary>
    public Result Bind(Func<T, Result> next) => _isSuccess ? next(_value) : Result.Failure(Error);

    /// <summary>This result, or the error of <paramref name="error"/> when <paramref name="predicate"/> is false for the value.</summary>
    public Result<T> Ensure(Func<T, bool> predicate, Func<T, Error> error) => _isSuccess && !predicate(_value) ? error(_value) : this;

    /// <summary>Runs <paramref name="action"/> with the value after a success and returns this result.</summary>
    public Result<T> Tap(Action<T> action)
    {
        if (_isSuccess)
        {
            action(_value);
        }

        return this;
    }

    /// <summary>Runs <paramref name="action"/> with the error after a failure and returns this result.</summary>
    public Result<T> TapError(Action<Error> action)
    {
        if (!_isSuccess)
        {
            action(Error);
        }

        return this;
    }

    /// <summary>The answer of <paramref name="onSuccess"/> or of <paramref name="onFailure"/>.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) => _isSuccess ? onSuccess(_value) : onFailure(Error);

    /// <inheritdoc />
    public bool Equals(Result<T> other)
        => _isSuccess == other._isSuccess
           && (_isSuccess ? EqualityComparer<T>.Default.Equals(_value, other._value) : Equals(Error, other.Error));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _isSuccess ? HashCode.Combine(true, _value) : HashCode.Combine(false, Error);

    /// <inheritdoc />
    public override string ToString() => _isSuccess ? $"Success({_value})" : $"Failure({Error})";

    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);
}