using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>Which invitation a token is for, as the store answers it across tenants: ids, and nothing else of it.</summary>
    /// <param name="Tenant">The tenant the invitation is into.</param>
    /// <param name="Invitation">The invitation.</param>
    /// <param name="IssuedBy">The seat that issued it, or that system work issued it for; <see langword="null"/> for none.</param>
    public sealed record InvitationOfToken<TInvitationId>(TTenantId Tenant, TInvitationId Invitation, TSeatId? IssuedBy)
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>;
}
