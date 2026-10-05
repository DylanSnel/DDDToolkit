namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The names of Tenancy's database functions: the contract between a package that writes them for one database,
/// the mapping of a module that reads Tenancy through them (<c>AddTenancyReadFunctions</c>), and the store that
/// asks them when <see cref="TenancyStoreOptions.DatabaseKeepsRights"/> is on. Each lives in the schema of the
/// context that maps Tenancy's tables, and answers under the column names given here, whatever the tables' own
/// columns are called and however their values are stored.
/// <para>
/// The first six answer the rows of the read model, one each, with the properties' names as the columns' and no
/// parameters. They run as their caller, so what a caller may read of Tenancy's tables decides their rows: a
/// seat its own rights and the rest of its tenant, system work in a tenant all of that tenant. A status is
/// answered as the name of its enum member. They answer access facts only, exactly the columns listed here:
/// none answers what a seat, a unit or a role is called, which the directory answers by id.
/// </para>
/// <para>
/// The next three answer a seat what it may learn of other seats' rights, and the three after them read across
/// tenants, for system work that began in no tenant. These six run as their owner, and answer ids, keys and
/// dates only. So does the last, which a database has where the context maps invitations: it reads across
/// tenants too, and answers which invitation a token's digest is for.
/// </para>
/// </summary>
public static class TenancyFunctionNames
{
    /// <summary>
    /// The rights the caller may read, as rows of <see cref="Access.SeatRight{TTenantId, TSeatId, TUnitId, TRoleId}"/>:
    /// <c>"TenantId"</c>, <c>"SeatId"</c>, <c>"UnitId"</c>, <c>"RoleId"</c>, <c>"Key"</c>, <c>"StartsAt"</c> and
    /// <c>"EndsAt"</c>.
    /// </summary>
    public const string CallerRights = "caller_rights";

    /// <summary>
    /// The closure of the organization's tree, as rows of <see cref="Access.OrganizationUnitPath{TTenantId, TUnitId}"/>:
    /// <c>"TenantId"</c>, <c>"AncestorId"</c>, <c>"DescendantId"</c> and <c>"Distance"</c>.
    /// </summary>
    public const string TenantUnitPaths = "tenant_unit_paths";

    /// <summary>
    /// The units, as rows of <see cref="Access.OrganizationUnitRow{TTenantId, TUnitId}"/>: <c>"Id"</c>,
    /// <c>"TenantId"</c>, <c>"ParentId"</c> and <c>"Status"</c>; never the name, nor a column the application added.
    /// </summary>
    public const string TenantUnits = "tenant_units";

    /// <summary>
    /// The roles, as rows of <see cref="Access.RoleRow{TTenantId, TRoleId}"/>: <c>"Id"</c>, <c>"TenantId"</c>,
    /// <c>"FromPack"</c>, <c>"Status"</c> and <c>"Keys"</c>, an array of text however the roles' table stores
    /// them; never the name.
    /// </summary>
    public const string TenantRoles = "tenant_roles";

    /// <summary>
    /// Where seats are placed, as rows of <see cref="Access.PlacementRow{TSeatId, TUnitId}"/>: <c>"SeatId"</c>,
    /// <c>"UnitId"</c> and <c>"IsPrimary"</c>, with the seat's <c>"TenantId"</c>, which the row is kept to its
    /// tenant by.
    /// </summary>
    public const string TenantPlacements = "tenant_placements";

    /// <summary>
    /// The seats, as rows of <see cref="Access.SeatRow{TTenantId, TSeatId}"/>: <c>"Id"</c>, <c>"TenantId"</c>
    /// and <c>"Status"</c>; never the identity or the display name.
    /// </summary>
    public const string TenantSeats = "tenant_seats";

    /// <summary>
    /// The tenant's administrators, as rows of <c>"SeatId"</c> and <c>"RoleId"</c>: no parameters. Answered to a
    /// seat that may read other seats' grants.
    /// </summary>
    public const string TenantAdministrators = "tenant_administrators";

    /// <summary>
    /// The rights a move of a unit changes, as rows of <c>"UnitId"</c>, <c>"Key"</c>, <c>"EndsAt"</c>,
    /// <c>"Parent"</c> and <c>"OfCaller"</c>: its parameters are the unit's parent and the parent it would get.
    /// Answered to a seat that manages units at both.
    /// </summary>
    public const string RightsAMoveChanges = "rights_a_move_changes";

    /// <summary>
    /// The seats that hold a key at a unit, as rows of <c>"SeatId"</c>: its parameters are the key and the unit.
    /// A seat that may read other seats' grants is answered every holder, any other seat itself when it holds.
    /// </summary>
    public const string SeatsHoldingAt = "seats_holding_at";

    /// <summary>
    /// The keys stored on the roles of every tenant, each once, as text: no parameters. One of the three reads
    /// across tenants: answered to Tenancy's own system work, begun in no tenant, and to nobody else.
    /// </summary>
    public const string RoleKeysInUse = "role_keys_in_use";

    /// <summary>
    /// The ids of the tenants a round of system work visits, the active and the suspended ones: no parameters.
    /// Answered to system work of any module, begun in no tenant, which then works in each tenant under its own
    /// scope: an id names no person and no row.
    /// </summary>
    public const string TenantsToSweep = "tenants_to_sweep";

    /// <summary>
    /// The seats a person has, in every tenant and whatever their status, as rows of <c>"TenantId"</c> and
    /// <c>"SeatId"</c>: its parameter is the person's verified identity. Answered to Tenancy's own system work,
    /// begun in no tenant, and to nobody else.
    /// </summary>
    public const string SeatsOfIdentity = "seats_of_identity";

    /// <summary>
    /// The invitation a token's digest is for, in whatever tenant, as a row of <c>"TenantId"</c>,
    /// <c>"InvitationId"</c> and <c>"IssuedBy"</c>: its parameter is the digest. Written only where the context
    /// maps invitations. Answered to Tenancy's own system work, begun in no tenant, and to nobody else: it is the
    /// one way a digest is read at all, and it answers ids for a digest somebody already has, never a digest.
    /// </summary>
    public const string InvitationOfDigest = "invitation_of_digest";
}
