using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// One of the tenant's active roles and a permission it grants: one answer of
/// <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.RolePermissions"/>.
/// </summary>
/// <param name="Role">The role.</param>
/// <param name="Permission">The permission it grants.</param>
public readonly record struct RoleWithPermission<TRoleId>(TRoleId Role, string Permission)
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
