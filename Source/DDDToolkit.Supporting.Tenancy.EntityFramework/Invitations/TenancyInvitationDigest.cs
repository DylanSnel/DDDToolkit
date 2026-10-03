using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The digest of an invitation's token, in a table of its own next to the invitations: one row per invitation,
/// written with it and never changed.
/// <para>
/// It is kept apart so that reading invitations never reads a digest. Whoever manages seats lists the invitations
/// into their units; the digest is of no use to them, and a table nobody is let read gives it to nobody. On
/// Postgres, <c>DDDToolkit.Supporting.Tenancy.Postgres</c> writes this table no policy that lets a caller read it:
/// whoever issues an invitation adds its row, and the one question asked of it, which invitation a digest is for,
/// is answered by a function.
/// </para>
/// </summary>
/// <typeparam name="TInvitationId">The application's invitation id.</typeparam>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
public sealed class TenancyInvitationDigest<TInvitationId, TTenantId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
{
    /// <summary>The invitation the token is for.</summary>
    public TInvitationId InvitationId { get; set; }

    /// <summary>The invitation's tenant, which the row is kept to its tenant by.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>The SHA-256 digest of the token's bytes: 32 bytes, unique.</summary>
    public byte[] Digest { get; set; } = [];
}
