namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// A ready-made role the application offers. A tenant is given a copy of every pack seeded for its shape when it
/// is provisioned, and the copy remembers the pack it came from; from then on the copy is the tenant's own to rename
/// and re-key. When the application changes the pack, the copy follows it once the host syncs the packs
/// (<c>services.SyncRolePacks()</c>): what the pack gained is added, what it lost is taken out, and what the
/// tenant changed itself stays.
/// </summary>
/// <param name="Key">The pack's key, unique in the catalogue, at most <see cref="MaxKeyLength"/> characters.</param>
/// <param name="Name">The name of the role made from it.</param>
/// <param name="Description">The description of the role made from it.</param>
/// <param name="Keys">
/// The keys the role grants; the keys they imply are added when the catalogue is built. An administrators'
/// pack may leave the list empty, and then holds every live key.
/// </param>
/// <param name="SeededFor">
/// The one shape of tenant that is given a copy, or <see langword="null"/>, the default, for every shape. It says
/// which tenants the pack is seeded into, not what the role is: a role has no shape. A tenant provisioned with
/// that shape gets a copy, and so does one that changes to it and has none yet; a tenant of the other shape never
/// gets one from the catalogue. A pack that <paramref name="SeedOnProvision"/> leaves out is seeded into no tenant.
/// </param>
/// <param name="Administers">
/// Whether this is the administrators' pack of the shapes it is seeded for: the first administrator is granted
/// it. Either it lists no keys, and then holds every live key of the catalogue once built, a key declared later
/// included, which the roles made from it before get when the host syncs the packs; or it lists keys, which must
/// include every one of Tenancy's live keys and every live key that manages access, directly or through a key
/// that implies it. With those an administrator runs the tenant's access and gives every role, since a role that
/// manages access is given only by a seat holding its keys that do, while the role itself holds no key to the
/// application's work that the pack does not list. The catalogue refuses a listing pack that leaves one out, so
/// marking another key stops the application at start-up until the pack lists it.
/// <para>
/// The list is what the administrators' role starts with, not a wall around the seat. An administrator still
/// puts any key into a role, and gives itself a role that manages no access, as every seat that manages grants
/// may; the grant records the seat that gave it.
/// </para>
/// <para>
/// An application that marks no pack as administering gets <see cref="TenancyPacks.DefaultAdministrators"/>,
/// which lists no keys, for every shape. One that marks a pack marks one for every shape: a single one with no
/// <paramref name="SeededFor"/>, or one for each shape, with that shape as its <paramref name="SeededFor"/>.
/// </para>
/// </param>
/// <param name="SeedOnProvision">Whether a newly provisioned tenant gets a copy.</param>
/// <param name="Order">Where it is listed; lower first.</param>
public sealed record RolePack(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> Keys,
    TenantShape? SeededFor = null,
    bool Administers = false,
    bool SeedOnProvision = true,
    int Order = 100)
{
    /// <summary>The longest key: what a storage keeps the pack a role was made from in.</summary>
    public const int MaxKeyLength = TenancyNames.MaxPackKeyLength;
}
