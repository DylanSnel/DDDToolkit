using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What the current caller may do, asked over one read source. Made by
/// <see cref="ITenancyAnswers{TTenantId, TSeatId, TUnitId, TRoleId}.Over"/>, which fixes the caller and the
/// moment "now" once, so every answer of one set of questions agrees with the others.
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
public interface ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>Every unit where the caller holds <paramref name="permission"/> now: the units it is held at, and every unit below them.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>: a typo in code.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<TUnitId> UnitsWhereIHold(string permission);

    /// <summary>
    /// Every pair of a unit and a permission of <paramref name="permissions"/> the caller holds there now: for each permission, the
    /// units it is held at and every unit below them. It is <see cref="UnitsWhereIHold"/> for several permissions in one
    /// query, so a list that shows what the caller may do with each row asks once, not once per permission:
    /// <code>
    /// var held = tenancy.WhereIHold(["projects.edit", "projects.close"]);
    /// var abilities = from project in projects
    ///                 join pair in held on project.UnitId equals pair.Unit
    ///                 select new { project.Id, pair.Key };
    /// </code>
    /// <para>
    /// The answer is for showing what a caller can do, a button or a menu. Whether a command may run is still
    /// asked when it runs, by the use case that runs it: a permission set read a moment ago decides nothing.
    /// </para>
    /// </summary>
    /// <param name="permissions">Permissions of the catalogue. A retired permission is held nowhere, and none at all answers nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="permissions"/> is null.</exception>
    /// <exception cref="ArgumentException">The catalogue does not know one of <paramref name="permissions"/>: a typo in code.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<UnitPermission<TUnitId>> WhereIHold(IReadOnlyCollection<string> permissions);

    /// <summary>The units the caller belongs to: each unit it is placed in, and every unit below it.</summary>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<TUnitId> ReadableUnits();

    /// <summary>
    /// <paramref name="unit"/> and every unit below it, in the caller's tenant: the part of the tree that hangs
    /// under one unit, read from the closure. A unit of another tenant, or of none, answers nothing.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<TUnitId> UnitsUnder(TUnitId unit);

    /// <summary>The tenant's active roles that grant <paramref name="permission"/>.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<TRoleId> RolesWithPermission(string permission);

    /// <summary>
    /// Every pair of one of the tenant's active roles and a permission of <paramref name="permissions"/> it grants:
    /// <see cref="RolesWithPermission"/> for several permissions in one query.
    /// </summary>
    /// <param name="permissions">Permissions of the catalogue. A retired permission is granted by no role here, and none at all answers nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="permissions"/> is null.</exception>
    /// <exception cref="ArgumentException">The catalogue does not know one of <paramref name="permissions"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<RoleWithPermission<TRoleId>> RolePermissions(IReadOnlyCollection<string> permissions);

    /// <summary>Every live permission the caller holds now that reaches <paramref name="unit"/>, each once. A retired permission is never among them.</summary>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<string> PermissionsIHoldAt(TUnitId unit);

    /// <summary>
    /// Who holds <paramref name="permission"/> at <paramref name="unit"/> now: the active seats with a right for it that
    /// applies now, held at the unit or at a unit above it. A seat learns about another seat's right only where it
    /// may read the grant that gives it: at a unit where it manages grants, seats or units, held there or above it,
    /// or anywhere when it manages roles for the whole tenant. So a seat that manages one part of the tree learns
    /// who holds the permission from within that part, and not who holds it from above; a seat that manages none learns
    /// only whether it holds the permission there itself. System work in a tenant learns about every seat of that tenant,
    /// and nobody about none.
    /// </summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <param name="unit">The unit.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>: a typo in code.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<TSeatId> SeatsHoldingPermissionAt(string permission, TUnitId unit);

    /// <summary>
    /// The tenant's units, archived ones included, as access facts, without a name: where each hangs and whether
    /// it is in use. What a unit is called is the directory's to answer, by id.
    /// </summary>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units();

    /// <summary>
    /// The tenant's roles, archived ones included, as access facts, without a name: the pack each was copied
    /// from, its status and the permissions it grants. What a role is called is the directory's to answer, by id.
    /// </summary>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<RoleRow<TTenantId, TRoleId>> Roles();

    /// <summary>
    /// The tenant's seats the caller reads, in any status, as access facts, without a name: whether each counts.
    /// What a seat is shown by is the application's, selected from its own seat, which the directory answers whole,
    /// by id.
    /// <para>
    /// A read of the seats, unlike the questions about rights: by default every seat of the tenant, and on Postgres
    /// the seats a read rule of the application's on its seat class lets the caller read, where it writes one
    /// (<c>tenant_seats()</c> runs as its caller). A module that admits a seat by asking here, as Membership's
    /// admission does, then admits only seats the caller reads.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    IQueryable<SeatRow<TTenantId, TSeatId>> Seats();

    /// <summary>Whether the caller holds <paramref name="permission"/> now for the whole tenant: at its root.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    Task<bool> HoldsAtTenantAsync(string permission, CancellationToken cancellationToken);

    /// <summary>Whether the caller holds <paramref name="permission"/> now at <paramref name="unit"/>, held there or above it.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    Task<bool> HoldsAtAsync(string permission, TUnitId unit, CancellationToken cancellationToken);
}
