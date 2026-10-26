using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// One unit of a tenant's organization: a company, a region, a site, whatever the application's organization is
/// made of. The application declares its own class with <see cref="OrganizationUnitAttribute{TUnitId}"/>.
/// <para>
/// A unit belongs to its organization, and nothing else changes it: every mutator here is
/// internal, and the organization checks the tree before it calls one.
/// </para>
/// <para>
/// It has no kind. Nothing about access reads what kind of unit a unit is, so Tenancy keeps none: an application
/// that tells regions from sites adds a field of its own to its class, an enum say, and sets it in the callback
/// the use cases take when they make a unit (<c>TenantToProvision.ConfigureRoot</c> and the <c>configure</c> of
/// <c>OrganizationCommands.AddUnitAsync</c>).
/// </para>
/// </summary>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
[EntityBase]
public abstract partial class OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
{
    /// <summary>The longest name a unit may have.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The unit this one hangs under, or <see langword="null"/> for the root.</summary>
    public TUnitId? ParentId { get; private set; }

    /// <summary>The unit's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Whether the unit is in use.</summary>
    public UnitStatus Status { get; private set; }

    /// <summary>Whether this is the organization's root.</summary>
    public bool IsRoot => ParentId is null;

    /// <summary>
    /// What a constructor would do: gives a new instance its id, place in the tree and name, and starts it active.
    /// Called once, by the organization, right after the instance is made.
    /// </summary>
    internal void InitializeNew(TUnitId id, TUnitId? parentId, string name)
    {
        var validName = TenancyNames.Required(name, TenancyNames.UnitNameToken, MaxNameLength);

        Id = id;
        ParentId = parentId;
        Name = validName;
        Status = UnitStatus.Active;
    }

    internal void Rename(string name) => Name = TenancyNames.Required(name, TenancyNames.UnitNameToken, MaxNameLength);

    internal void SetParent(TUnitId parentId) => ParentId = parentId;

    internal void Archive() => Status = UnitStatus.Archived;
}
