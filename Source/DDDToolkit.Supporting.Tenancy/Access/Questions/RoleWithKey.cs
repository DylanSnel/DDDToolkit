using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// One of the tenant's active roles and a key it grants: one answer of
/// <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.RoleKeys"/>.
/// </summary>
/// <param name="Role">The role.</param>
/// <param name="Key">The key it grants.</param>
public readonly record struct RoleWithKey<TRoleId>(TRoleId Role, string Key)
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
