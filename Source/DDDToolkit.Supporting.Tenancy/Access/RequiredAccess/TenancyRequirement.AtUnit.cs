using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// A permission is held by the caller at a unit: there, or at a unit above it, now. Otherwise
    /// <c>tenancy.not-permitted</c>, with the permission and the unit, exactly as the package's own managers refuse
    /// it; nobody is refused with its own reason first. System work in the tenant holds every permission at every
    /// unit of its tenant. A unit of another tenant, or one that does not exist, is one where nobody holds
    /// anything, and is refused the same way. A request declares it with <see cref="TenancyAccess.AtUnit{TUnitId}"/>.
    /// </summary>
    /// <remarks>
    /// For a command that acts at a unit the request names: one the package's manager decides, such as placing
    /// a seat or giving a role there, and one that makes something at a unit, where there is nothing yet a permission
    /// could be held on, such as opening a project there: the check a module would otherwise write in its
    /// handler, or as a case of its own. The unit is the request's own, so the unit checked is the unit the
    /// handler acts at. It costs one statement, on the context the check was registered over, as a permission for the
    /// whole tenant does.
    /// <code>
    /// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.AtUnit(BillingPermissions.OpenLedger, UnitId);
    /// </code>
    /// The unit's id is the application's own type, so the case is closed over it: the check registered with
    /// <c>AddTenancyAccess</c> decides it for the application's unit id, and stops a request that declares it
    /// over another type rather than letting it through.
    /// </remarks>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    public sealed record AtUnit<TUnitId> : TenancyRequirement
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        /// <summary>
        /// The requirement that <paramref name="permission"/> is held at <paramref name="unit"/>, there or above it. Made by
        /// <see cref="TenancyAccess.AtUnit{TUnitId}"/>, the one spelling a request writes.
        /// </summary>
        /// <param name="permission">The permission the request needs at the unit: one of the catalogue's.</param>
        /// <param name="unit">The unit, from the request.</param>
        /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
        internal AtUnit(string permission, TUnitId unit)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
            Permission = permission;
            Unit = unit;
        }

        /// <summary>The permission the request needs at the unit.</summary>
        public string Permission { get; }

        /// <summary>The unit the permission is needed at, from the request.</summary>
        public TUnitId Unit { get; }
    }
}
