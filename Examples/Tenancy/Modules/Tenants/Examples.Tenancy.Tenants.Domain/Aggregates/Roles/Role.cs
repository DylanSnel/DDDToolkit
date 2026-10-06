namespace Examples.Tenancy.Tenants.Domain.Aggregates.Roles;

/// <summary>
/// The application's role: a named set of permission keys in one tenant, the package's. Each tenant's roles start
/// as copies of the catalogue's packs, and are the tenant's own to rename, re-key and archive from then on. A role
/// made from a pack still follows its pack when the host syncs the packs: a key the pack gains later reaches it, and
/// what the tenant changed in it stays.
/// </summary>
/// <remarks>
/// A role of the organization: given at a unit, it counts for every unit below it, and the keys of a module it
/// holds reach whatever of that module is there, every project at the unit and below it among them. A role a
/// project's crew holds is not one of these: it is a project role, kept by the Projects module beside its projects.
/// </remarks>
[RoleAggregate<RoleId>]
public sealed partial class Role;
