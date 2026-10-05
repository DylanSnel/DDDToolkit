using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

// The rows the access questions read. They are plain classes with a parameterless constructor and public
// setters, so any storage can map them, in any context: Tenancy's own, and a consumer module's that reads
// them next to its own tables in one query. The aggregates stay the only way to change what they say.
//
// They carry what an access rule reads and nothing else: ids, keys, periods, statuses, a unit's parent, and a
// role's pack and keys. They never carry a text that is shown to people: no seat's display name, no unit's name,
// no role's name. A module that maps these rows can therefore not lean on Tenancy for what it shows;
// names are the directory's to answer, by id (TenancyUseCases<...>.TenancyDirectory). A property added here is
// a column every module's model maps and every read function answers, so a test pins the exact list.

/// <summary>
/// One key a seat holds at one unit through one role, for the period of the grant. Written by
/// <see cref="TenancyProjection.RightsOf{TTenantId, TSeatId, TUnitId, TRoleId}"/> whenever the seat or the
/// role changes; whether the period applies is asked when the row is read, so a grant expires without a
/// write. A seat that is not active, an archived role and a key that is not live have no rows.
/// </summary>
public sealed class SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The seat's tenant.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>The seat that holds the key.</summary>
    public TSeatId SeatId { get; set; }

    /// <summary>Where the role was granted; the key covers that unit and every unit below it.</summary>
    public TUnitId UnitId { get; set; }

    /// <summary>The role that grants it there.</summary>
    public TRoleId RoleId { get; set; }

    /// <summary>The permission key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The first moment the grant applies.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>The first moment it no longer applies, or <see langword="null"/> when it has no end.</summary>
    public DateTimeOffset? EndsAt { get; set; }
}
