namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// A ready-made role the application offers. A tenant is given a copy of every pack of its shape when it is
/// provisioned, and the copy remembers the pack it came from; from then on the copy is the tenant's own.
/// </summary>
/// <param name="Key">The pack's key, unique in the catalogue, at most <see cref="MaxKeyLength"/> characters.</param>
/// <param name="Name">The name of the role made from it.</param>
/// <param name="Description">The description of the role made from it.</param>
/// <param name="Keys">
/// The keys the role grants; the keys they imply are added when the catalogue is built. An administrators'
/// pack may leave the list empty, and then holds every live key.
/// </param>
/// <param name="Shape">The one shape of tenant it is for, or <see langword="null"/> for every shape.</param>
/// <param name="Administers">
/// Whether this is the administrators' pack of its shapes: the first administrator is granted it. Either it
/// lists no keys, and then holds every live key of the catalogue once built, a key declared later included;
/// or it lists keys, which must include every one of Tenancy's live keys and every live key that manages
/// access, directly or through a key that implies it. With those an administrator runs the tenant's access and
/// gives every role, since a role that manages access is given only by a seat holding its keys that do, while
/// the role itself holds no key to the application's work that the pack does not list. The catalogue refuses a
/// listing pack that leaves one out, so marking another key stops the application at start-up until the pack
/// lists it.
/// <para>
/// The list is what the administrators' role starts with, not a wall around the seat. An administrator still
/// puts any key into a role, and gives itself a role that manages no access, as every seat that manages grants
/// may; the grant records the seat that gave it.
/// </para>
/// </param>
/// <param name="SeedOnProvision">Whether a newly provisioned tenant gets a copy.</param>
/// <param name="Order">Where it is listed; lower first.</param>
public sealed record RolePack(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> Keys,
    TenantShape? Shape = null,
    bool Administers = false,
    bool SeedOnProvision = true,
    int Order = 100)
{
    /// <summary>The longest key: what a storage keeps the pack a role was made from in.</summary>
    public const int MaxKeyLength = TenancyNames.MaxPackKeyLength;
}
