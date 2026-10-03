using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Tenancy's questions as a row access rule asks them, so a module writes its rules on the seats, units and
/// roles Tenancy keeps without referencing anything of a database:
/// <code>
/// [RowAccess&lt;Report&gt;(RowOperations.Read, To = [RowAccessRoles.User])]
/// public static partial class ReportsAreReadWhereTheKeyIsHeld
/// {
///     public static bool Allows(Report report, Caller caller)
///         =&gt; TenancyRowAccess.UnitsWhereIHold&lt;OrganizationUnitId&gt;("reports.read").Contains(report.UnitId);
/// }
/// </code>
/// <para>
/// Only the database answers them: the SQL functions of the same names, which
/// <c>DDDToolkit.Supporting.Tenancy.Postgres</c> writes into the schema of the context that maps Tenancy's
/// tables, for the caller whose identity and tenant the connection carries. Called in C#, each throws. In C#,
/// ask <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}"/>, which gives the same answers to a
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
    /// The owner the functions are named relative to: <c>tenancy/units_where_i_hold</c>, whatever schema the
    /// application gives Tenancy's tables.
    /// </summary>
    public const string Owner = "tenancy";

    /// <summary>
    /// Every unit where the calling seat holds <paramref name="key"/> now: the units it is held at, and every unit
    /// below them. As <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.UnitsWhereIHold"/>.
    /// </summary>
    /// <param name="key">A key of the catalogue.</param>
    [AccessSet("units_where_i_hold")]
    public static partial AccessSet<TUnitId> UnitsWhereIHold<TUnitId>(string key)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>;

    /// <summary>
    /// The units the calling seat belongs to: each unit it is placed in, and every unit below it. As
    /// <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.ReadableUnits"/>.
    /// </summary>
    [AccessSet("readable_units")]
    public static partial AccessSet<TUnitId> ReadableUnits<TUnitId>()
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>;

    /// <summary>
    /// The active roles of the caller's tenant that grant <paramref name="key"/>. As
    /// <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.RolesWithKey"/>.
    /// </summary>
    /// <param name="key">A key of the catalogue.</param>
    [AccessSet("roles_with_key")]
    public static partial AccessSet<TRoleId> RolesWithKey<TRoleId>(string key)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>;

    /// <summary>
    /// The calling seat: the active seat of the caller's verified identity in the tenant the connection names,
    /// when that tenant is active too; <c>NULL</c> otherwise.
    /// </summary>
    [AccessScalar("caller_seat")]
    public static partial TSeatId CallerSeat<TSeatId>()
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>;

    /// <summary>The tenant of <see cref="CallerSeat{TSeatId}"/>; <c>NULL</c> when there is no such seat.</summary>
    [AccessScalar("caller_tenant")]
    public static partial TTenantId CallerTenant<TTenantId>()
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>Whether the calling seat holds <paramref name="key"/> now, at any unit.</summary>
    /// <param name="key">A key of the catalogue.</param>
    [AccessScalar("holds_key")]
    public static partial bool HoldsKey(string key);

    /// <summary>
    /// Whether the calling seat holds <paramref name="key"/> now for the whole tenant: at its root. As
    /// <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.HoldsTenantWideAsync"/>.
    /// </summary>
    /// <param name="key">A key of the catalogue.</param>
    [AccessScalar("holds_tenant_wide")]
    public static partial bool HoldsTenantWide(string key);

    /// <summary>
    /// The seat the caller's verified identity has in <paramref name="tenant"/>, while that seat and the tenant
    /// are active; <c>NULL</c> otherwise. As <see cref="CallerSeat{TSeatId}"/>, of a tenant given as an argument
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
    /// Whether the caller's seat in <paramref name="tenant"/> holds <paramref name="key"/> now, at any unit. As
    /// <see cref="HoldsKey"/>, of a tenant given as an argument.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="key">A key of the catalogue.</param>
    [AccessScalar("holds_key_in_tenant")]
    public static partial bool HoldsKeyInTenant<TTenantId>(TTenantId tenant, string key)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// Every unit where the caller's seat in <paramref name="tenant"/> holds <paramref name="key"/> now: the units
    /// it is held at, and every unit below them. As <see cref="UnitsWhereIHold{TUnitId}"/>, of a tenant given as
    /// an argument.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="key">A key of the catalogue.</param>
    [AccessSet("units_where_i_hold_in_tenant")]
    public static partial AccessSet<TUnitId> UnitsWhereIHoldInTenant<TUnitId, TTenantId>(TTenantId tenant, string key)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;

    /// <summary>
    /// The active roles of <paramref name="tenant"/> that grant <paramref name="key"/>, answered only to a caller
    /// with an active seat there, so the roles of a tenant stay unknown to everyone outside it. As
    /// <see cref="RolesWithKey{TRoleId}"/>, of a tenant given as an argument.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="key">A key of the catalogue.</param>
    [AccessSet("roles_with_key_in_tenant")]
    public static partial AccessSet<TRoleId> RolesWithKeyInTenant<TRoleId, TTenantId>(TTenantId tenant, string key)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>;
}
