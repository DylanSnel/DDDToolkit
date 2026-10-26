namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which keys a member's role can give on a resource, wherever the roles come from: the one cut the rules work out
/// from the resource's permissions, and that the C# questions and the functions in the database both apply. A role
/// that holds a key outside it gives nothing with that key on the resource.
/// <para>
/// Either the keys listed and no other, the permissions marked <c>InRoles</c> (<see cref="Excepts"/> false), or every
/// key but the ones listed, where the roles take keys of other modules (<see cref="OtherKeys"/>).
/// </para>
/// </summary>
public sealed class RoleKeys
{
    private readonly HashSet<string> _keys;

    internal RoleKeys(bool excepts, IEnumerable<string> keys)
    {
        Excepts = excepts;
        Keys = [.. keys.Distinct(StringComparer.Ordinal)];
        _keys = new HashSet<string>(Keys, StringComparer.Ordinal);
    }

    /// <summary>Whether <see cref="Keys"/> are the keys no role gives, rather than the only ones it does.</summary>
    public bool Excepts { get; }

    /// <summary>The keys listed, each once: the only ones a role gives, or the ones it never gives.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>Whether a member's role can give <paramref name="key"/>.</summary>
    /// <param name="key">A permission key.</param>
    public bool Allows(string key) => _keys.Contains(key) != Excepts;
}
