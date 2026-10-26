namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which keys of other modules a role gives on a resource besides the resource's own permissions: for roles that
/// are rows, a customer's or the application's own, which hold keys of every module that acts on the resource. A
/// crew's role on a project, say, gives the key to record an inspection there, which the module of inspections
/// declares and the module of projects never names.
/// <code>
/// Roles.Custom(starters: [...], otherKeys: OtherKeys.AllBut(OrganizationKeys.RolesManage))
/// </code>
/// Without it a role gives the resource's permissions marked <c>InRoles</c> and nothing else. With it, a role gives
/// those and every other key it holds, except the resource's permissions marked <c>InRoles = false</c>, the keys the
/// classes that declare the resource's permissions declare at another level, and the keys listed here.
/// </summary>
public sealed class OtherKeys
{
    private OtherKeys(string[] excepted)
    {
        if (excepted.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A permission key is not blank.", nameof(excepted));
        }

        Excepted = [.. excepted.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>A role gives every key of another module it holds.</summary>
    public static OtherKeys All { get; } = new([]);

    /// <summary>A role gives every key of another module it holds but these.</summary>
    /// <param name="keys">The keys no role gives on the resource, such as the keys that manage the organization itself.</param>
    /// <exception cref="ArgumentException">One of <paramref name="keys"/> is blank.</exception>
    public static OtherKeys AllBut(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return new OtherKeys(keys);
    }

    /// <summary>The keys no role gives on the resource, each once.</summary>
    public IReadOnlyList<string> Excepted { get; }
}
