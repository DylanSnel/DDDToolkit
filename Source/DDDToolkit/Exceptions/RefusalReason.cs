using System.Globalization;
using DDDToolkit.Validation;

namespace DDDToolkit.Exceptions;

/// <summary>
/// Why one case of a combined requirement refused the caller: the code and the arguments of its own refusal. A
/// <see cref="ToolkitRefusals.Refused"/> made at the door, of a request that lets one caller or another through and met
/// neither, carries one for each case, in their order, as its <see cref="ToolkitRefusals.ReasonsArgument"/>:
/// <code>
/// catch (RefusalException refused) when (refused.Code == ToolkitRefusals.Refused
///     &amp;&amp; refused.Arguments.TryGetValue(ToolkitRefusals.ReasonsArgument, out var value)
///     &amp;&amp; value is IReadOnlyList&lt;RefusalReason&gt; reasons)
/// {
///     // reasons[0].Code is "tenancy.operators-only", reasons[1].Code "tenancy.not-permitted", with its Key
/// }
/// </code>
/// </summary>
/// <remarks>
/// Its message is left out on purpose: a client phrases a reason from its code, as it phrases any refusal, in the
/// reader's language. Two reasons are equal when their codes and arguments are, however the arguments were built, and
/// the reasons of a combination nested in another are compared one by one. Written for a log it is the code and the
/// arguments by name, <c>tenancy.not-permitted (Key = tenancy.seats.manage)</c>, and a nested combination's reasons in
/// brackets, <c>access.refused (Reasons = [access.not-own; tenancy.not-permitted (Key = tenancy.seats.manage)])</c>.
/// </remarks>
public sealed record RefusalReason
{
    /// <summary>A reason of <paramref name="code"/>, with <paramref name="arguments"/>, copied.</summary>
    /// <param name="code">The code of the case's refusal.</param>
    /// <param name="arguments">The values its message was built from, by name; none when left out.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null or blank.</exception>
    public RefusalReason(string code, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = code;
        Arguments = FailureArguments.Copy(arguments);
    }

    /// <summary>The code of the case's refusal, which a client branches on and translates.</summary>
    public string Code { get; }

    /// <summary>The values its message was built from, by name, matched without regard to case. Never <see langword="null"/>.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; }

    /// <summary>The reason of <paramref name="refusal"/>: its code and its arguments.</summary>
    /// <param name="refusal">A case's refusal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="refusal"/> is null.</exception>
    public static RefusalReason From(RefusalException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new RefusalReason(refusal.Code, refusal.Arguments);
    }

    /// <inheritdoc />
    public bool Equals(RefusalReason? other)
        => other is not null && string.Equals(Code, other.Code, StringComparison.Ordinal) && ArgumentsAreEqual(Arguments, other.Arguments);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Agrees with Equals: the names without regard to case or to their order, and a nested combination's reasons by
        // what they hold, not by the list they are kept in.
        var hash = 0;
        foreach (var (name, value) in Arguments)
        {
            hash ^= HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(name), value is IEnumerable<RefusalReason> reasons ? HashOf(reasons) : value);
        }

        return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Code), hash);
    }

    /// <summary>The code, and the arguments by name, ordered by name: <c>tenancy.not-permitted (Key = tenancy.seats.manage)</c>.</summary>
    public override string ToString()
        => Arguments.Count == 0
            ? Code
            : Code + " (" + string.Join(", ", Arguments.OrderBy(static each => each.Key, StringComparer.OrdinalIgnoreCase).Select(static each => each.Key + " = " + Text(each.Value))) + ")";

    private static string Text(object? value) => value switch
    {
        null => "null",
        IEnumerable<RefusalReason> reasons => "[" + string.Join("; ", reasons) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// <see cref="FailureArguments.AreEqual"/>, with a nested combination's reasons compared one by one, in their order: the
    /// list a refusal keeps them in is its own, and no other refusal's is that list.
    /// </summary>
    private static bool ArgumentsAreEqual(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other))
            {
                return false;
            }

            var equal = value is IEnumerable<RefusalReason> reasons && other is IEnumerable<RefusalReason> others
                ? reasons.SequenceEqual(others)
                : Equals(value, other);
            if (!equal)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A hash of <paramref name="reasons"/> that agrees with comparing them one by one.</summary>
    private static int HashOf(IEnumerable<RefusalReason> reasons)
    {
        var hash = new HashCode();
        foreach (var reason in reasons)
        {
            hash.Add(reason);
        }

        return hash.ToHashCode();
    }
}
