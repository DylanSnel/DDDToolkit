using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

// The rows Tenancy's database functions answer, as the model maps them: to no table and no view, so they are
// read only from the SQL that asks the function. They are in the model, rather than made up for one query,
// because Entity Framework converts the application's ids only for types the model knows.

/// <summary>One administrator of a tenant, as <see cref="TenancyFunctionNames.TenantAdministrators"/> answers it.</summary>
internal sealed class TenantAdministratorRow<TSeatId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The seat.</summary>
    public TSeatId SeatId { get; set; }

    /// <summary>The role that makes it an administrator.</summary>
    public TRoleId RoleId { get; set; }
}

/// <summary>One seat that holds a key at a unit, as <see cref="TenancyFunctionNames.SeatsHoldingAt"/> answers it.</summary>
internal sealed class SeatHolderRow<TSeatId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    /// <summary>The seat.</summary>
    public TSeatId SeatId { get; set; }
}

/// <summary>The invitation a token's digest is for, as <see cref="TenancyFunctionNames.InvitationOfDigest"/> answers it.</summary>
internal sealed class InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    /// <summary>The tenant the invitation is into.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>The invitation.</summary>
    public TInvitationId InvitationId { get; set; }

    /// <summary>The seat that issued it, or that system work issued it for; <see langword="null"/> for none.</summary>
    public TSeatId? IssuedBy { get; set; }
}

/// <summary>One tenant a round of system work visits, as <see cref="TenancyFunctionNames.TenantsToSweep"/> answers it.</summary>
internal sealed class TenantToSweepRow<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
{
    /// <summary>The tenant.</summary>
    public TTenantId TenantId { get; set; }
}
