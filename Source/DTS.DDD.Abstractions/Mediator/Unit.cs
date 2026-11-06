#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// The answer of a request that answers with nothing, as <see cref="ICommand"/> and <see cref="IRequest"/> do. Every
/// <see cref="Unit"/> equals every other.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
    private static readonly Unit _value;

    /// <summary>The one value.</summary>
    public static ref readonly Unit Value => ref _value;

    /// <summary>A completed task that answers with <see cref="Value"/>, for a handler that does its work synchronously.</summary>
    public static ValueTask<Unit> ValueTask => new(_value);

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    /// <inheritdoc />
    int IComparable.CompareTo(object? obj) => 0;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <summary>Every <see cref="Unit"/> equals every other.</summary>
    public static bool operator ==(Unit first, Unit second) => true;

    /// <summary>No <see cref="Unit"/> differs from another.</summary>
    public static bool operator !=(Unit first, Unit second) => false;

    /// <inheritdoc />
    public override string ToString() => "()";
}
#endif
