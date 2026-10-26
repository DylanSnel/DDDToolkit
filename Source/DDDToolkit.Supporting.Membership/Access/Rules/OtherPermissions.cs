namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which permissions of other modules a role gives on a resource besides the resource's own permissions: for roles that
/// are rows, a customer's or the application's own, which hold permissions of every module that acts on the resource. A
/// crew's role on a project, say, gives the permission to record an inspection there, which the module of inspections
/// declares and the module of projects never names.
/// <code>
/// Roles.Custom(starters: [...], otherPermissions: OtherPermissions.AllBut(OrganizationPermissions.RolesManage))
/// </code>
/// Without it a role gives the resource's permissions marked <c>InRoles</c> and nothing else. With it, a role gives
/// those and every other permission it holds, except the resource's permissions marked <c>InRoles = false</c>, the permissions the
/// classes that declare the resource's permissions declare at another level, and the permissions listed here.
/// </summary>
public sealed class OtherPermissions
{
    private OtherPermissions(string[] excepted)
    {
        if (excepted.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A permission is not blank.", nameof(excepted));
        }

        Excepted = [.. excepted.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>A role gives every permission of another module it holds.</summary>
    public static OtherPermissions All { get; } = new([]);

    /// <summary>A role gives every permission of another module it holds but these.</summary>
    /// <param name="permissions">The permissions no role gives on the resource, such as the permissions that manage the organization itself.</param>
    /// <exception cref="ArgumentException">One of <paramref name="permissions"/> is blank.</exception>
    public static OtherPermissions AllBut(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return new OtherPermissions(permissions);
    }

    /// <summary>The permissions no role gives on the resource, each once.</summary>
    public IReadOnlyList<string> Excepted { get; }
}
