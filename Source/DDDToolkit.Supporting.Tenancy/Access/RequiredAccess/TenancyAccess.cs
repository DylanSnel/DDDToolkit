using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// How a request spells what it requires where Tenancy is the one that knows, beside the core's own
/// (<c>AccessRequirement.SignedIn()</c> and the like) and Membership's (<c>MemberAccess.On(key, resource)</c>):
/// <code>
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.InTenant();
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.ForTheWholeTenant(TenancyKeys.RolesManage);
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.AtUnit(TenancyKeys.GrantsManage, Unit);
/// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.RequiresOperator();
/// </code>
/// Each answers one of <see cref="TenancyRequirement"/>'s cases, which the check that
/// <c>services.AddTenancyAccess&lt;TRequests, TContext&gt;()</c> adds holds the caller to.
/// </summary>
/// <remarks>
/// Methods rather than constructors, so a case closed over the application's unit id takes it from the
/// argument: <c>AtUnit(key, unit)</c>, not <c>new TenancyRequirement.AtUnit&lt;OrganizationUnitId&gt;(key, unit)</c>.
/// </remarks>
public static class TenancyAccess
{
    /// <summary>
    /// The caller works in the tenant the request was sent for: a seat there, or system work there
    /// (<see cref="TenancyRequirement.InTenant"/>).
    /// </summary>
    public static TenancyRequirement.InTenant InTenant() => new();

    /// <summary>
    /// The caller holds <paramref name="key"/> for the whole tenant, at its root, now
    /// (<see cref="TenancyRequirement.ForTheWholeTenant"/>).
    /// </summary>
    /// <param name="key">The key the request needs for the whole tenant: one of the catalogue's.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    public static TenancyRequirement.ForTheWholeTenant ForTheWholeTenant(string key) => new(key);

    /// <summary>
    /// The caller holds <paramref name="key"/> at <paramref name="unit"/>, there or at a unit above it, now
    /// (<see cref="TenancyRequirement.AtUnit{TUnitId}"/>).
    /// </summary>
    /// <remarks>
    /// The type is taken from the argument, so the compiler does not tell a unit from another id: on a request
    /// that also carries a seat, <c>AtUnit(key, Seat)</c> compiles. It lets nobody through, since the check
    /// decides the case for the application's unit id alone and stops every send of another with an
    /// <see cref="InvalidOperationException"/> that names it. Where a request carries more ids than one, write
    /// the type, <c>AtUnit&lt;OrganizationUnitId&gt;(key, Unit)</c>, and the compiler catches the wrong one; a test
    /// that holds every request to the requirement it should declare catches it as well.
    /// </remarks>
    /// <param name="key">The key the request needs at the unit: one of the catalogue's.</param>
    /// <param name="unit">The unit, from the request.</param>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    public static TenancyRequirement.AtUnit<TUnitId> AtUnit<TUnitId>(string key, TUnitId unit)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        => new(key, unit);

    /// <summary>
    /// The caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no
    /// system work (<see cref="TenancyRequirement.Operator"/>). For queries only.
    /// </summary>
    /// <remarks>
    /// Named as <c>AccessRequirement.RequiresSystemWork()</c> is, the other case that lets exactly one kind of
    /// caller through.
    /// </remarks>
    public static TenancyRequirement.Operator RequiresOperator() => new();
}
