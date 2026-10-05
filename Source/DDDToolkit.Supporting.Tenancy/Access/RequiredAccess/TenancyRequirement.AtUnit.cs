using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// A key is held by the caller at a unit: there, or at a unit above it, now. Otherwise
    /// <c>tenancy.not-permitted</c>, with the key and the unit, exactly as the package's own use cases refuse
    /// it; nobody is refused with its own reason first. System work in the tenant holds every key at every
    /// unit of its tenant. A unit of another tenant, or one that does not exist, is one where nobody holds
    /// anything, and is refused the same way. A request declares it with <see cref="TenancyAccess.AtUnit{TUnitId}"/>.
    /// </summary>
    /// <remarks>
    /// For a command that acts at a unit the request names: one the package's use case decides, such as placing
    /// a seat or giving a role there, and one that makes something at a unit, where there is nothing yet a key
    /// could be held on, such as opening a project there: the check a module would otherwise write in its
    /// handler, or as a case of its own. The unit is the request's own, so the unit checked is the unit the
    /// handler acts at. It costs one statement, on the context the check was registered over, as a key for the
    /// whole tenant does.
    /// <code>
    /// AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.AtUnit(BillingKeys.OpenLedger, UnitId);
    /// </code>
    /// The unit's id is the application's own type, so the case is closed over it: the check registered with
    /// <c>AddTenancyAccess</c> decides it for the application's unit id, and stops a request that declares it
    /// over another type rather than letting it through.
    /// </remarks>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    public sealed record AtUnit<TUnitId> : TenancyRequirement
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        /// <summary>The requirement that <paramref name="key"/> is held at <paramref name="unit"/>, there or above it.</summary>
        /// <param name="key">The key the request needs at the unit: one of the catalogue's.</param>
        /// <param name="unit">The unit, from the request.</param>
        /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
        public AtUnit(string key, TUnitId unit)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            Key = key;
            Unit = unit;
        }

        /// <summary>The key the request needs at the unit.</summary>
        public string Key { get; }

        /// <summary>The unit the key is needed at, from the request.</summary>
        public TUnitId Unit { get; }
    }
}
