namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Tenancy's own tables, as <see cref="TenancyModel.TableOf"/> names them.</summary>
public enum TenancyTable
{
    /// <summary>The tenants.</summary>
    Tenant,

    /// <summary>Each tenant's organization.</summary>
    Organization,

    /// <summary>The organizations' units.</summary>
    Unit,

    /// <summary>The closure of each organization's tree.</summary>
    UnitPath,

    /// <summary>The seats.</summary>
    Seat,

    /// <summary>Where each seat is placed.</summary>
    Placement,

    /// <summary>The roles each placement grants.</summary>
    Grant,

    /// <summary>The keys each seat holds where, written from the seats and the roles.</summary>
    Right,

    /// <summary>The roles.</summary>
    Role,

    /// <summary>One row per tenant that every change of rights takes first.</summary>
    AccessRevision,

    /// <summary>The invitations into a tenant, in a model that maps them with <c>AddTenancyInvitations</c>.</summary>
    Invitation,

    /// <summary>The digests of the invitations' tokens, kept apart from the invitations.</summary>
    InvitationDigest,
}
