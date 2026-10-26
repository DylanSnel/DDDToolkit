using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>A unit of a tenant's organization, as a row to read: where it hangs and whether it is in use, never its name.</summary>
public sealed class OrganizationUnitRow<TTenantId, TUnitId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
{
    /// <summary>The unit's id.</summary>
    public TUnitId Id { get; set; }

    /// <summary>The organization's tenant.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>The unit it hangs under, or <see langword="null"/> for the root.</summary>
    public TUnitId? ParentId { get; set; }

    /// <summary>Whether the unit is in use.</summary>
    public UnitStatus Status { get; set; }
}
