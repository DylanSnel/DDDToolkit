namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which permissions a member's role can give on a resource, wherever the roles come from: the one cut the rules work out
/// from the resource's permissions, and that the C# questions and the functions in the database both apply. A role
/// that holds a permission outside it gives nothing with that permission on the resource.
/// <para>
/// Either the permissions listed and no other, the permissions marked <c>InRoles</c> (<see cref="Excepts"/> false), or every
/// permission but the ones listed, where the roles take permissions of other modules (<see cref="OtherPermissions"/>).
/// </para>
/// </summary>
public sealed class RolePermissions
{
    private readonly HashSet<string> _keys;

    internal RolePermissions(bool excepts, IEnumerable<string> permissions)
    {
        Excepts = excepts;
        Permissions = [.. permissions.Distinct(StringComparer.Ordinal)];
        _keys = new HashSet<string>(Permissions, StringComparer.Ordinal);
    }

    /// <summary>Whether <see cref="Permissions"/> are the permissions no role gives, rather than the only ones it does.</summary>
    public bool Excepts { get; }

    /// <summary>The permissions listed, each once: the only ones a role gives, or the ones it never gives.</summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>Whether a member's role can give <paramref name="permission"/>.</summary>
    /// <param name="permission">A permission.</param>
    public bool Allows(string permission) => _keys.Contains(permission) != Excepts;
}
