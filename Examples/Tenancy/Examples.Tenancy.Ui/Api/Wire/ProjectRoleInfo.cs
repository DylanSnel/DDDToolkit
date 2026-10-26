namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A project role: one of the tenant's own roles for its crews, the rows of <c>GET /project-roles</c>. A crew names
/// the roles its members hold by these ids.
/// </summary>
/// <remarks>
/// Anybody who works in the tenant reads what a project role is called. Its permissions the API answers to whoever manages
/// the tenant's roles, and <c>null</c> to anybody else; the same people make, rename and archive one, and set its permissions.
/// </remarks>
/// <param name="Id">The role.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for; empty when nobody said.</param>
/// <param name="MadeFrom">The starter role it was made from, such as <c>crew-lead</c>, or <see langword="null"/> for one the tenant made.</param>
/// <param name="Status"><c>active</c>, or <c>archived</c> for one that can no longer be given.</param>
/// <param name="Permissions">The permissions it gives on a crew, or <see langword="null"/> when the API did not answer them.</param>
public sealed record ProjectRoleInfo(Guid Id, string Name, string? Description, string? MadeFrom, string Status, IReadOnlyList<string>? Permissions)
{
    /// <summary>The permissions it gives; none when the API did not answer them.</summary>
    public IReadOnlyList<string> PermissionsShown => Permissions ?? [];

    /// <summary>Whether the role is in use; an archived one stays on whoever holds it, gives nothing and is given no more.</summary>
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
}
