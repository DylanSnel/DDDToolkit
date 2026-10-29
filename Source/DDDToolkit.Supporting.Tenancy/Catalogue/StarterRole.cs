namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// A ready-made role the application offers. A tenant is given a copy of every starter role seeded for its shape when it
/// is provisioned, and the copy remembers the starter role it came from; from then on the copy is the tenant's own to rename
/// and to give other permissions. When the application changes the starter role, the copy follows it once the host syncs the starter roles
/// (<c>services.SyncStarterRoles()</c>): what the starter role gained is added, what it lost is taken out, and what the
/// tenant changed itself stays.
/// </summary>
/// <param name="Key">The starter role's key, unique in the catalogue, at most <see cref="MaxKeyLength"/> characters.</param>
/// <param name="Name">The name of the role made from it.</param>
/// <param name="Description">The description of the role made from it.</param>
/// <param name="Permissions">
/// The permissions the role grants; the permissions they imply are added when the catalogue is built. An administrators'
/// starter role may leave the list empty, and then holds every live permission.
/// </param>
/// <param name="SeededFor">
/// The one shape of tenant that is given a copy, or <see langword="null"/>, the default, for every shape. It says
/// which tenants the starter role is seeded into, not what the role is: a role has no shape. A tenant provisioned with
/// that shape gets a copy, and so does one that changes to it and has none yet; a tenant of the other shape never
/// gets one from the catalogue. A starter role that <paramref name="SeedOnProvision"/> leaves out is seeded into no tenant.
/// </param>
/// <param name="Administers">
/// Whether this is the administrators' starter role of the shapes it is seeded for: the first administrator is granted
/// it. Either it lists no permissions, and then holds every live permission of the catalogue once built, a permission declared later
/// included, which the roles made from it before get when the host syncs the starter roles; or it lists permissions, which must
/// include every one of Tenancy's live permissions and every live permission that manages access, directly or through a permission
/// that implies it. With those an administrator runs the tenant's access and gives every role, since a role that
/// manages access is given only by a seat holding its permissions that do, while the role itself holds no permission to the
/// application's work that the starter role does not list. The catalogue refuses a listing starter role that leaves one out, so
/// marking another permission stops the application at start-up until the starter role lists it.
/// <para>
/// The list is what the administrators' role starts with, not a wall around the seat. An administrator still
/// puts any permission into a role, and gives itself a role that manages no access, as every seat that manages grants
/// may; the grant records the seat that gave it.
/// </para>
/// <para>
/// An application that marks no starter role as administering gets <see cref="TenancyStarterRoles.DefaultAdministrators"/>,
/// which lists no permissions, for every shape. One that marks a starter role marks one for every shape: a single one with no
/// <paramref name="SeededFor"/>, or one for each shape, with that shape as its <paramref name="SeededFor"/>.
/// </para>
/// </param>
/// <param name="SeedOnProvision">Whether a newly provisioned tenant gets a copy.</param>
/// <param name="Order">Where it is listed; lower first.</param>
public sealed record StarterRole(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> Permissions,
    TenantShape? SeededFor = null,
    bool Administers = false,
    bool SeedOnProvision = true,
    int Order = 100)
{
    /// <summary>The longest permission: what a storage keeps the starter role a role was made from in.</summary>
    public const int MaxKeyLength = TenancyNames.MaxStarterRoleKeyLength;
}
