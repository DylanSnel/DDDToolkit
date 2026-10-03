using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>A tenant's role, as a row to read: its pack, its status and the keys it grants, never its name.</summary>
public sealed class RoleRow<TTenantId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The role's id.</summary>
    public TRoleId Id { get; set; }

    /// <summary>The role's tenant.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>The key of the pack it was copied from, or <see langword="null"/>.</summary>
    public string? FromPack { get; set; }

    /// <summary>Whether the role is in use.</summary>
    public RoleStatus Status { get; set; }

    /// <summary>The permission keys it grants.</summary>
    public IReadOnlyList<string> Keys { get; set; } = [];
}
