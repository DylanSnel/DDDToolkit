namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A role of the organization, with the pack it was copied from, its keys, and whether it manages access: then only
/// someone holding its keys that do gives it, never to themselves. The rows of <c>GET /tenancy/roles</c>, of
/// <c>POST /tenancy/directory/roles</c> and of the roles in <c>GET /me</c>. A role a crew holds is a project role,
/// <see cref="ProjectRoleInfo"/>.
/// </summary>
/// <remarks>
/// The keys of a role a seat holds itself are always there, in <c>GET /me</c>. In the lists of the tenant's roles
/// the API answers them to whoever manages the roles, and <c>null</c> to anybody else.
/// </remarks>
public sealed record RoleInfo(Guid Id, string Name, string? FromPack, string Status, IReadOnlyList<string>? Keys, bool ManagesAccess = false)
{
    /// <summary>The keys it brings; none when the API did not answer them.</summary>
    public IReadOnlyList<string> KeysShown => Keys ?? [];

    /// <summary>Whether the role is active; an archived one gives nothing.</summary>
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
}
