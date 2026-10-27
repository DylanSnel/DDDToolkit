using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What the current caller may do, asked over one read source. Made by <c>db.Tenancy()</c> over a context, or by
/// <see cref="TenancyAccessManager{TTenantId, TSeatId, TUnitId, TRoleId}.In"/> over another read source. Either fixes the
/// caller and the moment "now" once, so every answer of one set of queries agrees with the others.
/// <para>
/// The queries are not run here: a module composes them into its own query, where they become subqueries,
/// or runs them itself. A permission granted at the root holds for the whole tenant; one granted lower down covers
/// its unit and every unit below it. A grant counts from its start until its end, and a permission the catalogue
/// has retired holds nowhere.
/// </para>
/// <para>
/// A seat is answered from its own rights. System work in a tenant holds every permission in that tenant and
/// nothing in another. Nobody holds nothing. System work outside any tenant has nothing to ask about here,
/// and asking is a programming error.
/// </para>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
public interface ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>Every unit where the caller holds <paramref name="permission"/> now: the units it is held at, and every unit below them.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <returns>A query of the units' ids, which the module composes into its own query.</returns>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>: a typo in code.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// The projects the caller may edit, in one statement:
    /// <code>
    /// var editable = db.Tenancy().UnitsWhereIHold("projects.edit");
    /// var projects = await db.Projects.Where(project =&gt; editable.Contains(project.UnitId)).ToListAsync(cancellationToken);
    /// </code>
    /// </example>
    IQueryable<TUnitId> UnitsWhereIHold(string permission);

    /// <summary>
    /// Every pair of a unit and a permission of <paramref name="permissions"/> the caller holds there now: for each permission, the
    /// units it is held at and every unit below them. It is <see cref="UnitsWhereIHold(string)"/> for several permissions in one
    /// query, so a list that shows what the caller may do with each row asks once, not once per permission.
    /// <para>
    /// The answer is for showing what a caller can do, a button or a menu. The manager that runs a command checks the
    /// permission again when the command runs.
    /// </para>
    /// </summary>
    /// <param name="permissions">Permissions of the catalogue. A retired permission is held nowhere, and none at all answers nothing.</param>
    /// <returns>A query of pairs of a unit and a permission held there, which the module composes into its own query.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="permissions"/> is null.</exception>
    /// <exception cref="ArgumentException">The catalogue does not know one of <paramref name="permissions"/>: a typo in code.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var held = db.Tenancy().UnitsWhereIHold(["projects.edit", "projects.close"]);
    /// var abilities = from project in db.Projects
    ///                 join pair in held on project.UnitId equals pair.Unit
    ///                 select new { project.Id, pair.Permission };
    /// </code>
    /// </example>
    IQueryable<UnitPermission<TUnitId>> UnitsWhereIHold(IReadOnlyCollection<string> permissions);

    /// <summary>The units the caller belongs to: each unit it is placed in, and every unit below it.</summary>
    /// <returns>A query of the units' ids.</returns>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var mine = db.Tenancy().ReadableUnits();
    /// var nearby = db.Inspections.Where(inspection =&gt; mine.Contains(inspection.UnitId));
    /// </code>
    /// </example>
    IQueryable<TUnitId> ReadableUnits();

    /// <summary>
    /// <paramref name="unit"/> and every unit below it, in the caller's tenant: the part of the tree that hangs
    /// under one unit, read from the closure. A unit of another tenant, or of none, answers nothing.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <returns>A query of the units' ids, <paramref name="unit"/> included.</returns>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// The projects at North and every unit below it, such as North Coast:
    /// <code>
    /// var north = db.Tenancy().UnitsUnder(command.Unit);
    /// var projects = db.Projects.Where(project =&gt; north.Contains(project.UnitId));
    /// </code>
    /// </example>
    IQueryable<TUnitId> UnitsUnder(TUnitId unit);

    /// <summary>The tenant's active roles that grant <paramref name="permission"/>.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <returns>A query of the roles' ids.</returns>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// The roles a crew lead could be given, for a picker:
    /// <code>
    /// var leading = await db.Tenancy().RolesWithPermission("projects.crew.manage").ToListAsync(cancellationToken);
    /// </code>
    /// </example>
    IQueryable<TRoleId> RolesWithPermission(string permission);

    /// <summary>
    /// Every pair of one of the tenant's active roles and a permission of <paramref name="permissions"/> it grants:
    /// <see cref="RolesWithPermission(string)"/> for several permissions in one query.
    /// </summary>
    /// <param name="permissions">Permissions of the catalogue. A retired permission is granted by no role here, and none at all answers nothing.</param>
    /// <returns>A query of pairs of a role and a permission it grants.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="permissions"/> is null.</exception>
    /// <exception cref="ArgumentException">The catalogue does not know one of <paramref name="permissions"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var pairs = await db.Tenancy().RolesWithPermission(["projects.edit", "projects.close"]).ToListAsync(cancellationToken);
    /// </code>
    /// </example>
    IQueryable<RoleWithPermission<TRoleId>> RolesWithPermission(IReadOnlyCollection<string> permissions);

    /// <summary>Every live permission the caller holds now that reaches <paramref name="unit"/>, each once. A retired permission is never among them.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>A query of the permissions.</returns>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var atNorthCoast = await db.Tenancy().PermissionsIHoldAt(project.UnitId).ToListAsync(cancellationToken);
    /// </code>
    /// </example>
    IQueryable<string> PermissionsIHoldAt(TUnitId unit);

    /// <summary>
    /// Who holds <paramref name="permission"/> at <paramref name="unit"/> now: the active seats with a right for it that
    /// applies now, held at the unit or at a unit above it.
    /// <para>
    /// A seat sees another seat's right only where it may read the grant that gives it. That is at a unit where it manages
    /// grants, seats or units, held there or above it, or anywhere when it manages roles for the whole tenant. A seat that
    /// manages one part of the tree sees who holds the permission from within that part, and not who holds it from above.
    /// A seat that manages nothing sees only whether it holds the permission there itself. System work in a tenant sees
    /// every seat of that tenant. Nobody sees none.
    /// </para>
    /// </summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <param name="unit">The unit.</param>
    /// <returns>A query of the seats' ids.</returns>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>: a typo in code.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// Who may close projects at North Coast, for the list of people a closure can be handed to:
    /// <code>
    /// var closers = await db.Tenancy().SeatsHoldingPermissionAt("projects.close", northCoast).ToListAsync(cancellationToken);
    /// </code>
    /// </example>
    IQueryable<TSeatId> SeatsHoldingPermissionAt(string permission, TUnitId unit);

    /// <summary>
    /// The tenant's units, archived ones included, as access facts, without a name: where each hangs and whether
    /// it is in use. What a unit is called comes from <c>OrganizationManager.UnitsByIdAsync</c>, by id.
    /// </summary>
    /// <returns>A query of the units' rows.</returns>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var active = db.Tenancy().Units().Where(unit =&gt; unit.Status == UnitStatus.Active).Select(unit =&gt; unit.Id);
    /// </code>
    /// </example>
    IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units();

    /// <summary>
    /// The tenant's roles, archived ones included, as access facts, without a name: the starter role each was copied
    /// from, its status and the permissions it grants. What a role is called comes from <c>RoleManager.ByIdAsync</c>, by id.
    /// </summary>
    /// <returns>A query of the roles' rows.</returns>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var fromStarterRoles = db.Tenancy().Roles().Where(role =&gt; role.FromStarterRole != null);
    /// </code>
    /// </example>
    IQueryable<RoleRow<TTenantId, TRoleId>> Roles();

    /// <summary>
    /// The tenant's seats the caller reads, in any status, as access facts, without a name: whether each counts.
    /// What a seat is shown by is the application's, selected from its own seat class, which <c>SeatManager.ByIdAsync</c>
    /// returns whole, by id.
    /// <para>
    /// This reads the seats, where the other queries read rights. By default it returns every seat of the tenant. On
    /// Postgres it returns the seats that a read rule of the application's on its seat class lets the caller read, where
    /// the application writes one (<c>tenant_seats()</c> runs as its caller). A module that admits a seat by asking here,
    /// as Membership's admission does, then admits only seats the caller reads.
    /// </para>
    /// </summary>
    /// <returns>A query of the seats' rows.</returns>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var seated = await db.Tenancy().Seats().AnyAsync(seat =&gt; seat.Id == command.Seat, cancellationToken);
    /// </code>
    /// </example>
    IQueryable<SeatRow<TTenantId, TSeatId>> Seats();

    /// <summary>Whether the caller holds <paramref name="permission"/> now for the whole tenant: at its root.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// <see langword="true"/> when the caller holds the permission at the root. System work in the tenant holds every live
    /// permission. A retired permission, and a caller who is nobody, give <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var showSettings = await db.Tenancy().HoldsAtTenantAsync(TenancyPermissions.SettingsManage, cancellationToken);
    /// </code>
    /// </example>
    Task<bool> HoldsAtTenantAsync(string permission, CancellationToken cancellationToken);

    /// <summary>Whether the caller holds <paramref name="permission"/> now at <paramref name="unit"/>, held there or above it.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// <see langword="true"/> when the caller holds the permission at the unit or above it. System work in the tenant holds
    /// every live permission at every unit of its tenant. A retired permission, a unit of another tenant, and a caller who
    /// is nobody give <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var mayEdit = await db.Tenancy().HoldsAtAsync("projects.edit", project.UnitId, cancellationToken);
    /// </code>
    /// </example>
    Task<bool> HoldsAtAsync(string permission, TUnitId unit, CancellationToken cancellationToken);
}
