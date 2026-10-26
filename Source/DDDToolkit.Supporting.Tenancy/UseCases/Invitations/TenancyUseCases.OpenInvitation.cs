using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>An invitation that can still be accepted, as the people who manage seats at its unit see it.</summary>
    /// <param name="Id">The invitation.</param>
    /// <param name="Address">The address it is for.</param>
    /// <param name="UnitId">The unit the seat would be placed in.</param>
    /// <param name="RoleId">The role the seat would hold there.</param>
    /// <param name="GrantUntil">When that grant would end, or <see langword="null"/> for no end.</param>
    /// <param name="IssuedAt">When it was issued.</param>
    /// <param name="ExpiresAt">The first moment it can no longer be accepted.</param>
    /// <param name="IssuedBy">The seat that issued it, or that system work issued it for; <see langword="null"/> for none.</param>
    /// <param name="IssuedAsSystem">Whether system work issued it.</param>
    public sealed record OpenInvitation<TInvitationId>(
        TInvitationId Id,
        string Address,
        TUnitId UnitId,
        TRoleId RoleId,
        DateTimeOffset? GrantUntil,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        TSeatId? IssuedBy,
        bool IssuedAsSystem)
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>;
}
