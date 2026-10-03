using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// One unit of a tenant's organization: a company, a region, a site, whatever the application's unit kinds
/// are. The application declares its own class with <see cref="OrganizationUnitAttribute{TUnitId}"/>.
/// <para>
/// A unit belongs to its organization, and nothing else changes it: every mutator here is
/// internal, and the organization checks the tree before it calls one. The kind is a label from the
/// application's catalogue; nothing about access reads it.
/// </para>
/// </summary>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
[EntityBase]
public abstract partial class OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
{
    /// <summary>The longest name a unit may have.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The longest unit kind.</summary>
    public const int MaxKindLength = TenancyNames.MaxUnitKindLength;

    /// <summary>The unit this one hangs under, or <see langword="null"/> for the root.</summary>
    public TUnitId? ParentId { get; private set; }

    /// <summary>The unit's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The kind of unit it is, one of the application's unit kinds.</summary>
    public string Kind { get; private set; } = string.Empty;

    /// <summary>Whether the unit is in use.</summary>
    public UnitStatus Status { get; private set; }

    /// <summary>Whether this is the organization's root.</summary>
    public bool IsRoot => ParentId is null;

    /// <summary>
    /// What a constructor would do: gives a new instance its id, place in the tree, name and kind, and starts
    /// it active. Called once, by the organization, right after the instance is made.
    /// </summary>
    internal void InitializeNew(TUnitId id, TUnitId? parentId, string name, string kind)
    {
        var validName = TenancyNames.Required(name, TenancyNames.UnitNameToken, MaxNameLength);
        var validKind = ValidKind(kind);

        Id = id;
        ParentId = parentId;
        Name = validName;
        Kind = validKind;
        Status = UnitStatus.Active;
    }

    internal void Rename(string name) => Name = TenancyNames.Required(name, TenancyNames.UnitNameToken, MaxNameLength);

    internal void SetParent(TUnitId parentId) => ParentId = parentId;

    internal void Archive() => Status = UnitStatus.Archived;

    /// <summary>A kind trimmed, or <c>tenancy.kind-invalid</c> when blank or longer than <see cref="MaxKindLength"/>.</summary>
    internal static string ValidKind(string? kind)
    {
        var trimmed = kind?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || trimmed.Length > MaxKindLength
            ? throw TenancyRefusals.Of(TenancyRefusals.KindInvalid, ("Kind", kind ?? string.Empty), ("Max", MaxKindLength))
            : trimmed;
    }
}
