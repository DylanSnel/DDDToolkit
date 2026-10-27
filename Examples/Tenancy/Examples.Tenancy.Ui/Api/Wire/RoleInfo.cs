namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A role of the organization, with the starter role it was copied from, its permissions, and whether it manages access: then only
/// someone holding its permissions that do gives it, never to themselves. The rows of <c>GET /tenancy/roles</c>, of
/// <c>POST /tenancy/directory/roles</c> and of the roles in <c>GET /me</c>. A role a crew holds is a project role,
/// <see cref="ProjectRoleInfo"/>.
/// </summary>
/// <remarks>
/// In the lists of the tenant's roles the API answers the permissions to whoever manages the roles, and <c>null</c> to
/// anybody else. The roles in <c>GET /me</c> are named, without their permissions and without whether they manage
/// access: the list of the tenant's roles says both.
/// </remarks>
public sealed record RoleInfo(Guid Id, string Name, string? FromStarterRole, string Status, IReadOnlyList<string>? Permissions = null, bool ManagesAccess = false)
{
    /// <summary>The permissions it brings; none when the API did not answer them.</summary>
    public IReadOnlyList<string> PermissionsShown => Permissions ?? [];

    /// <summary>Whether the role is active; an archived one gives nothing.</summary>
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
}
