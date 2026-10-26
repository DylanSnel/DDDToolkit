namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which permission keys a member can hold on a resource through a role: the one list that says how far a
/// member's role reaches. A role that holds a key outside it gives nothing with that key on the resource.
/// <para>
/// One declaration on purpose. The same question is asked in C#, where a request is checked, and in the
/// database, where rows are filtered, and two lists written apart drift apart.
/// </para>
/// </summary>
public sealed class MemberKeys
{
    private readonly HashSet<string> _keys;

    private MemberKeys(bool excepts, string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A permission key is not blank.", nameof(keys));
        }

        Excepts = excepts;
        Keys = [.. keys.Distinct(StringComparer.Ordinal)];
        _keys = new HashSet<string>(Keys, StringComparer.Ordinal);
    }

    /// <summary>A member's role gives these keys and no other.</summary>
    /// <param name="keys">The keys.</param>
    /// <exception cref="ArgumentException">One of <paramref name="keys"/> is blank.</exception>
    public static MemberKeys Only(params string[] keys) => new(excepts: false, keys);

    /// <summary>
    /// A member's role gives every key it holds but these: for a resource whose roles are kept elsewhere and
    /// hold keys of many modules, where naming what a member never gets is shorter than naming everything it
    /// does. A key that is declared later is then given unless it is added here.
    /// </summary>
    /// <param name="keys">The keys no member's role gives.</param>
    /// <exception cref="ArgumentException">One of <paramref name="keys"/> is blank.</exception>
    public static MemberKeys AllBut(params string[] keys) => new(excepts: true, keys);

    /// <summary>Whether <see cref="Keys"/> are the exceptions (<see cref="AllBut"/>) rather than the whole list (<see cref="Only"/>).</summary>
    public bool Excepts { get; }

    /// <summary>The keys listed, each once: the only ones given, or the ones never given.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>Whether a member's role can give <paramref name="key"/>.</summary>
    /// <param name="key">A permission key.</param>
    public bool Allows(string key) => _keys.Contains(key) != Excepts;
}
