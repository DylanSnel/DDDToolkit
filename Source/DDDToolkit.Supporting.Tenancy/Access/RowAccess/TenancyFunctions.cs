using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Tenancy's functions in a database that answers the questions itself, as another package names them: the rules
/// of a resource with members, say, whose own functions ask Tenancy's.
/// <code>
/// MembershipRules.For&lt;Board&gt;(roles, above: new ReachFromAbove(TenancyFunctions.UnitsWhereIHold))
/// </code>
/// Each is named by its owner, <see cref="TenancyRowAccess.Owner"/>, and its name, whatever schema the application
/// gives Tenancy's tables: the script that writes a function that asks one finds it where it is defined.
/// <c>DDDToolkit.Supporting.Tenancy.Postgres</c> defines them.
/// </summary>
public static class TenancyFunctions
{
    /// <summary>
    /// Answers the calling seat: the active seat of the caller's verified identity in the tenant the connection
    /// names, or <c>NULL</c> for a caller without one. As <see cref="TenancyRowAccess.CallerSeat{TSeatId}"/>.
    /// </summary>
    public static DatabaseFunction CallerSeat { get; } = new(TenancyRowAccess.Owner, "caller_seat");

    /// <summary>
    /// Answers, for a key, the units the calling seat's hold of it reaches: where it is held, and every unit below.
    /// As <see cref="TenancyRowAccess.UnitsWhereIHold{TUnitId}"/>.
    /// </summary>
    public static DatabaseFunction UnitsWhereIHold { get; } = new(TenancyRowAccess.Owner, "units_where_i_hold");

    /// <summary>
    /// Answers, for a key, the roles of the caller's tenant in use that give it. As
    /// <see cref="TenancyRowAccess.RolesWithKey{TRoleId}"/>.
    /// </summary>
    public static DatabaseFunction RolesWithKey { get; } = new(TenancyRowAccess.Owner, "roles_with_key");
}
