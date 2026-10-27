using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Tenancy's questions as a row access rule asks them, so a module writes its rules on the seats, units and
/// roles Tenancy keeps without referencing anything of a database:
/// <code>
/// [RowAccess&lt;Report&gt;(RowOperations.Read, To = [RowAccessRoles.User])]
/// public static partial class ReportsAreReadWhereThePermissionIsHeld
/// {
///     public static bool Allows(Report report, Caller caller)
///         =&gt; TenancyRowAccess.UnitsWhereIHold&lt;OrganizationUnitId&gt;("reports.read").Contains(report.UnitId);
/// }
/// </code>
/// <para>
/// Only the database answers them: the SQL functions of the same names, which
/// <c>DDDToolkit.Supporting.Tenancy.Postgres</c> writes into the schema of the context that maps Tenancy's
/// tables, for the caller whose identity and tenant the connection carries. Called in C#, each throws. In C#,
/// ask <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}"/>, which gives the same answers to a
/// seat.
/// </para>
/// <para>
/// The functions answer for a seat: a signed-in user's, found from the verified identity and the tenant. A
/// suspended or deactivated seat holds nothing, and neither does any seat of a suspended or closed tenant; a
/// question about the caller answers <c>NULL</c> for someone without an active seat there, which no comparison
/// passes. A rule for anonymous callers that asks one is refused when the policies are written. System work in a
/// tenant has no seat, so every question answers nothing for it, where the C# questions answer the whole tenant:
/// the policies Tenancy writes keep system work to its tenant themselves, so a rule for it asks none of these.
/// </para>
/// <para>
/// The same questions say who reads Tenancy's own seats and invitations, where the application replaces Tenancy's
/// default with a read rule on its class: <c>seat.TenantId == CallerTenant&lt;TenantId&gt;()</c> for the caller's
/// tenant, <c>seat.Id == CallerSeatId&lt;SeatId&gt;()</c> for the caller itself,
/// <c>UnitsWhereIHold&lt;OrganizationUnitId&gt;(permission).Contains(invitation.UnitId)</c> for where it holds a permission,
/// <see cref="HoldsPermission"/> and <see cref="HoldsAtTenant"/>, and <see cref="SeatsInMyUnits{TSeatId}"/> for the
/// seats placed where it is placed, or below.
/// </para>
/// <para>
/// The questions named <c>...InTenant</c> take the tenant as an argument, and read no setting of the
/// connection: for a policy that runs where the application's connection is not the one asking, such as one on
/// the path of a stored file or on a channel, whose tenant is in the path or the channel's name. They answer
/// for the seat the caller's verified identity has in that tenant, while that seat and the tenant are active,
/// and nothing to anyone else. A set-shaped one is worked out once per statement, so its tenant comes from the
/// parameters of an access function of the module's own, which a policy then asks about each row.
/// </para>
/// </summary>
[AccessFunctions(Owner = Owner)]
public static partial class TenancyRowAccess
{
    /// <summary>
    /// The owner the functions are named relative to: <c>tenancy/units_where_i_hold_permission</c>, whatever schema the
    /// application gives Tenancy's tables.
    /// </summary>
    public const string Owner = "tenancy";

    /// <summary>
    /// Every unit where the calling seat holds <paramref name="permission"/> now: the units it is held at, and every unit
    /// below them. As <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.UnitsWhereIHold(string)"/>.
    /// </summary>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("units_where_i_hold_permission")]
    public static partial AccessSet<TUnitId> UnitsWhereIHold<TUnitId>(string permission)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>;

    /// <summary>
    /// The units the calling seat belongs to: each unit it is placed in, and every unit below it. As
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.ReadableUnits"/>.
    /// </summary>
    [AccessSet("readable_units")]
    public static partial AccessSet<TUnitId> ReadableUnits<TUnitId>()
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>;

    /// <summary>
    /// The seats placed at one of <see cref="ReadableUnits{TUnitId}"/>: at a unit the calling seat is placed at, or at
    /// a unit below one, whatever their status, and the calling seat itself once it is placed anywhere. So a seat
    /// placed at a parent finds the people of every unit below it too, and one placed at the root, as every seat of
    /// a flat tenant is, finds every placed seat of the tenant. For a read rule on the application's seat class,
    /// which says who reads which seats:
    /// <code>
    /// [RowAccess&lt;Seat&gt;(RowOperations.Read)]
    /// public static partial class MembersReadThePeopleOfTheirUnits
    /// {
    ///     public static bool Allows(Seat seat, Caller caller) =&gt; TenancyRowAccess.SeatsInMyUnits&lt;SeatId&gt;().Contains(seat.Id);
    /// }
    /// </code>
    /// </summary>
    [AccessSet("seats_in_my_units")]
    public static partial AccessSet<TSeatId> SeatsInMyUnits<TSeatId>()
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>;

    /// <summary>
    /// The active roles of the caller's tenant that grant <paramref name="permission"/>. As
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.RolesWithPermission(string)"/>.
    /// </summary>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("roles_with_permission")]
    public static partial AccessSet<TRoleId> RolesWithPermission<TRoleId>(string permission)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>;

    /// <summary>
    /// The id of the calling seat: the active seat of the caller's verified identity in the tenant the connection
    /// names, when that tenant is active too; <c>NULL</c> otherwise. A rule of any module asks it, also one whose
    /// context does not map the seats: <c>inspection.RecordedBy == TenancyRowAccess.CallerSeatId&lt;SeatId&gt;()</c>.
    /// </summary>
    [AccessScalar("caller_seat")]
    public static partial TSeatId CallerSeatId<TSeatId>()
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>;

    /// <summary>
    /// The calling seat itself, the application's own seat class, whose fields a rule reads as it reads its own row's:
    /// <code>
    /// [RowAccess&lt;Organization&gt;(RowOperations.Read, Restrictive = true)]
    /// public static partial class OnlyEmployeesReadTheOrganization
    /// {
    ///     public static bool Allows(Organization organization, Caller caller)
    ///         =&gt; TenancyRowAccess.CallerSeat&lt;Seat&gt;().Kind == SeatKind.Employee;
    /// }
    /// </code>
    /// A field is <c>(SELECT q1."Kind" FROM "tenancy".caller_seat_row() q1)</c> in the policy, the column the model maps
    /// it to; an enum or a value object compared with it is written as that column stores it. The function runs as its
    /// owner, so a rule on the seats themselves may read it too, and a seat reads its own fields whatever a read rule
    /// on the seats says. For a caller without a seat it answers no row, so every field is <c>NULL</c>, and a
    /// comparison with one is SQL's own: such a caller never passes it, negated or not. Only a rule of the context that
    /// maps the seats can ask it; a rule of another module asks <see cref="CallerSeatId{TSeatId}"/>.
    /// </summary>
    /// <typeparam name="TSeat">The application's seat class, the one declared with <c>[SeatAggregate]</c> (DDD00101 for another).</typeparam>
    [AccessRow("caller_seat_row")]
    public static partial TSeat CallerSeat<TSeat>()
        where TSeat : class;

    /// <summary>The tenant of <see cref="CallerSeatId{TSeatId}"/>; <c>NULL</c> when there is no such seat.</summary>
    [AccessScalar("caller_tenant")]
    public static partial TTenantId CallerTenant<TTenantId>()
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>Whether the calling seat holds <paramref name="permission"/> now, at any unit.</summary>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessScalar("holds_permission")]
    public static partial bool HoldsPermission(string permission);

    /// <summary>
    /// Whether the calling seat holds <paramref name="permission"/> now for the whole tenant: at its root. As
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.HoldsAtTenantAsync"/>.
    /// </summary>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessScalar("holds_at_tenant")]
    public static partial bool HoldsAtTenant(string permission);

    /// <summary>
    /// The seat the caller's verified identity has in <paramref name="tenant"/>, while that seat and the tenant
    /// are active; <c>NULL</c> otherwise. As <see cref="CallerSeatId{TSeatId}"/>, of a tenant given as an argument
    /// rather than the one the connection names.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    [AccessScalar("seat_in_tenant")]
    public static partial TSeatId SeatInTenant<TSeatId, TTenantId>(TTenantId tenant)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>Whether the caller has an active seat in <paramref name="tenant"/>, and the tenant is active.</summary>
    /// <param name="tenant">The tenant asked about.</param>
    [AccessScalar("seated_in_tenant")]
    public static partial bool SeatedInTenant<TTenantId>(TTenantId tenant)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// Whether the caller's seat in <paramref name="tenant"/> holds <paramref name="permission"/> now, at any unit. As
    /// <see cref="HoldsPermission"/>, of a tenant given as an argument.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessScalar("holds_permission_in_tenant")]
    public static partial bool HoldsPermissionInTenant<TTenantId>(TTenantId tenant, string permission)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// Every unit where the caller's seat in <paramref name="tenant"/> holds <paramref name="permission"/> now: the units
    /// it is held at, and every unit below them. As <see cref="UnitsWhereIHold{TUnitId}"/>, of a tenant given as
    /// an argument.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("units_where_i_hold_permission_in_tenant")]
    public static partial AccessSet<TUnitId> UnitsWhereIHoldInTenant<TUnitId, TTenantId>(TTenantId tenant, string permission)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// The active roles of <paramref name="tenant"/> that grant <paramref name="permission"/>, answered only to a caller
    /// with an active seat there, so the roles of a tenant stay unknown to everyone outside it. As
    /// <see cref="RolesWithPermission{TRoleId}"/>, of a tenant given as an argument.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("roles_with_permission_in_tenant")]
    public static partial AccessSet<TRoleId> RolesWithPermissionInTenant<TRoleId, TTenantId>(TTenantId tenant, string permission)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;
}
