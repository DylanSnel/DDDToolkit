using System.Collections.ObjectModel;

namespace DDDToolkit.Validation;

/// <summary>
/// The named values a failure carries next to its message, shared by <see cref="ValidationError"/>,
/// <see cref="DDDToolkit.Invariants.InvariantViolation"/> and <see cref="DDDToolkit.Invariants.InvariantFailure"/>.
/// <para>
/// A failure is compared by what it says, so two failures with the same arguments are equal however the
/// dictionaries holding them were built. That is what keeps the records' equality honest now that one of
/// their members is a reference type with reference equality.
/// </para>
/// </summary>
internal static class FailureArguments
{
    /// <summary>The arguments of a failure that has none. Shared, so a failure without arguments allocates nothing for them.</summary>
    public static readonly IReadOnlyDictionary<string, object?> None
        = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(0));

    /// <summary>
    /// Takes a private copy, so the caller's dictionary can change afterwards without the failure
    /// changing with it. Names are matched without regard to case, because a template written as
    /// <c>{maxLength}</c> should not miss an argument added as <c>MaxLength</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Copy(IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        if (arguments is null)
        {
            return None;
        }

        var copy = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in arguments)
        {
            ArgumentNullException.ThrowIfNull(name, nameof(arguments));
            copy[name] = value;
        }

        return copy.Count == 0 ? None : new ReadOnlyDictionary<string, object?>(copy);
    }

    /// <summary>A copy of <paramref name="arguments"/> with <paramref name="name"/> set to <paramref name="value"/>.</summary>
    public static IReadOnlyDictionary<string, object?> With(IReadOnlyDictionary<string, object?> arguments, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        var copy = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase)
        {
            [name] = value,
        };

        return new ReadOnlyDictionary<string, object?>(copy);
    }

    /// <summary>True when both hold the same names with equal values.</summary>
    public static bool AreEqual(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other) || !Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A hash that agrees with <see cref="AreEqual"/>: it ignores the order the names were added in.</summary>
    public static int GetHashCode(IReadOnlyDictionary<string, object?> arguments)
    {
        var hash = 0;
        foreach (var (name, value) in arguments)
        {
            hash ^= HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(name), value);
        }

        return hash;
    }
}
