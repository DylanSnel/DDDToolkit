using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>A tenant's role, as a row to read: its starter role, its status and the permissions it grants, never its name.</summary>
public sealed class RoleRow<TTenantId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The role's id.</summary>
    public TRoleId Id { get; set; }

    /// <summary>The role's tenant.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>The key of the starter role it was copied from, or <see langword="null"/>.</summary>
    public string? FromStarterRole { get; set; }

    /// <summary>Whether the role is in use.</summary>
    public RoleStatus Status { get; set; }

    /// <summary>The permissions it grants.</summary>
    public IReadOnlyList<string> Permissions { get; set; } = [];
}
