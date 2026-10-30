using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Tenancy's questions as a row access rule asks them, of its user, so a module writes its rules on the seats, units and
/// roles Tenancy keeps without referencing anything of a database:
/// <code>
/// [RowAccess&lt;Report&gt;(RowOperations.Read, To = [RowAccessRoles.User])]
/// public static partial class ReportsAreReadWhereThePermissionIsHeld
/// {
///     public static bool Allows(Report report, IRowUser user)
///         =&gt; user.UnitsWhereIHold("reports.read").Contains(report.UnitId);
/// }
/// </code>
/// <para>
/// The class is in the namespace of <see cref="IRowUser"/>, so the <c>using</c> a rule has for its user is the only one it
/// needs. Each question is generic over the application's ids, and a question whose type parameters are all ids has a
/// wrapper without type arguments in every project of a module that declares Tenancy's classes, or says which of its ids
/// are Tenancy's with <c>[assembly: TenancyIds&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;]</c>. The generic form
/// keeps working: <c>user.UnitsWhereIHold&lt;OrganizationUnitId&gt;(permission)</c>.
/// </para>
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
/// default with a read rule on its class: <c>seat.TenantId == user.CurrentTenant()</c> for the caller's
/// tenant, <c>seat.Id == user.CurrentSeatId()</c> for the caller itself,
/// <c>user.UnitsWhereIHold(permission).Contains(invitation.UnitId)</c> for where it holds a permission,
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
    /// <param name="user">The rule's user.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("units_where_i_hold_permission")]
    [TemplateRegistration(Shorthand = true)]
    public static partial AccessSet<TUnitId> UnitsWhereIHold<[TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId>(this IRowUser user, string permission)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>;

    /// <summary>
    /// The units the calling seat belongs to: each unit it is placed in, and every unit below it. As
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.ReadableUnits"/>.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    [AccessSet("readable_units")]
    [TemplateRegistration(Shorthand = true)]
    public static partial AccessSet<TUnitId> ReadableUnits<[TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId>(this IRowUser user)
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
    ///     public static bool Allows(Seat seat, IRowUser user) =&gt; user.SeatsInMyUnits().Contains(seat.Id);
    /// }
    /// </code>
    /// </summary>
    /// <param name="user">The rule's user.</param>
    [AccessSet("seats_in_my_units")]
    [TemplateRegistration(Shorthand = true)]
    public static partial AccessSet<TSeatId> SeatsInMyUnits<[TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId>(this IRowUser user)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>;

    /// <summary>
    /// The active roles of the caller's tenant that grant <paramref name="permission"/>. As
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.RolesWithPermission(string)"/>.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("roles_with_permission")]
    [TemplateRegistration(Shorthand = true)]
    public static partial AccessSet<TRoleId> RolesWithPermission<[TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(this IRowUser user, string permission)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>;

    /// <summary>
    /// The id of the calling seat: the active seat of the caller's verified identity in the tenant the connection
    /// names, when that tenant is active too; <c>NULL</c> otherwise. A rule of any module asks it, also one whose
    /// context does not map the seats: <c>inspection.RecordedBy == user.CurrentSeatId()</c>.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    [AccessScalar("current_seat")]
    [TemplateRegistration(Shorthand = true)]
    public static partial TSeatId CurrentSeatId<[TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId>(this IRowUser user)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>;

    /// <summary>
    /// The calling seat itself, the application's own seat class, whose fields a rule reads as it reads its own row's:
    /// <code>
    /// [RowAccess&lt;Organization&gt;(RowOperations.Read, Restrictive = true)]
    /// public static partial class OnlyEmployeesReadTheOrganization
    /// {
    ///     public static bool Allows(Organization organization, IRowUser user)
    ///         =&gt; user.CurrentSeat&lt;Seat&gt;().Kind == SeatKind.Employee;
    /// }
    /// </code>
    /// A field is <c>(SELECT q1."Kind" FROM "tenancy".current_seat_row() q1)</c> in the policy, the column the model maps
    /// it to; an enum or a value object compared with it is written as that column stores it. The function runs as its
    /// owner, so a rule on the seats themselves may read it too, and a seat reads its own fields whatever a read rule
    /// on the seats says. For a caller without a seat it answers no row, so every field is <c>NULL</c>, and a
    /// comparison with one is SQL's own: such a caller never passes it, negated or not. Only a rule of the context that
    /// maps the seats can ask it; a rule of another module asks <see cref="CurrentSeatId{TSeatId}"/>. It keeps its type
    /// argument, because the generator needs the row's type to name its columns.
    /// </summary>
    /// <typeparam name="TSeat">The application's seat class, the one declared with <c>[SeatAggregate]</c> (DDD00101 for another).</typeparam>
    /// <param name="user">The rule's user.</param>
    [AccessRow("current_seat_row")]
    public static partial TSeat CurrentSeat<TSeat>(this IRowUser user)
        where TSeat : class;

    /// <summary>The tenant of <see cref="CurrentSeatId{TSeatId}"/>; <c>NULL</c> when there is no such seat.</summary>
    /// <param name="user">The rule's user.</param>
    [AccessScalar("current_tenant")]
    [TemplateRegistration(Shorthand = true)]
    public static partial TTenantId CurrentTenant<[TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(this IRowUser user)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>Whether the calling seat holds <paramref name="permission"/> now, at any unit.</summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessScalar("holds_permission")]
    public static partial bool HoldsPermission(this IRowUser user, string permission);

    /// <summary>
    /// Whether the calling seat holds <paramref name="permission"/> now for the whole tenant: at its root. As
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.HoldsAtTenantAsync"/>.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessScalar("holds_at_tenant")]
    public static partial bool HoldsAtTenant(this IRowUser user, string permission);

    /// <summary>
    /// The seat the caller's verified identity has in <paramref name="tenant"/>, while that seat and the tenant
    /// are active; <c>NULL</c> otherwise. As <see cref="CurrentSeatId{TSeatId}"/>, of a tenant given as an argument
    /// rather than the one the connection names.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="tenant">The tenant asked about.</param>
    [AccessScalar("seat_in_tenant")]
    [TemplateRegistration(Shorthand = true)]
    public static partial TSeatId SeatInTenant<[TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId, [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(this IRowUser user, TTenantId tenant)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>Whether the caller has an active seat in <paramref name="tenant"/>, and the tenant is active.</summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="tenant">The tenant asked about.</param>
    [AccessScalar("seated_in_tenant")]
    [TemplateRegistration(Shorthand = true)]
    public static partial bool SeatedInTenant<[TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(this IRowUser user, TTenantId tenant)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// Whether the caller's seat in <paramref name="tenant"/> holds <paramref name="permission"/> now, at any unit. As
    /// <see cref="HoldsPermission"/>, of a tenant given as an argument.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessScalar("holds_permission_in_tenant")]
    [TemplateRegistration(Shorthand = true)]
    public static partial bool HoldsPermissionInTenant<[TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(this IRowUser user, TTenantId tenant, string permission)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// Every unit where the caller's seat in <paramref name="tenant"/> holds <paramref name="permission"/> now: the units
    /// it is held at, and every unit below them. As <see cref="UnitsWhereIHold{TUnitId}"/>, of a tenant given as
    /// an argument.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("units_where_i_hold_permission_in_tenant")]
    [TemplateRegistration(Shorthand = true)]
    public static partial AccessSet<TUnitId> UnitsWhereIHoldInTenant<[TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId, [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(this IRowUser user, TTenantId tenant, string permission)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// The active roles of <paramref name="tenant"/> that grant <paramref name="permission"/>, answered only to a caller
    /// with an active seat there, so the roles of a tenant stay unknown to everyone outside it. As
    /// <see cref="RolesWithPermission{TRoleId}"/>, of a tenant given as an argument.
    /// </summary>
    /// <param name="user">The rule's user.</param>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    [AccessSet("roles_with_permission_in_tenant")]
    [TemplateRegistration(Shorthand = true)]
    public static partial AccessSet<TRoleId> RolesWithPermissionInTenant<[TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId, [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(this IRowUser user, TTenantId tenant, string permission)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;
}
