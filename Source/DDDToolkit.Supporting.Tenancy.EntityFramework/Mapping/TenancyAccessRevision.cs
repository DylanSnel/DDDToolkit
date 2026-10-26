using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// One row per tenant that every change of rights in it reads and bumps, before it checks anything. Its
/// <see cref="Revision"/> is a concurrency token, so of two changes that each decided on what the other was
/// about to change, the second to save fails instead of both being written. The seats, roles and units such
/// a change touches are different rows, and their own versions could not see the conflict.
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
public sealed class TenancyAccessRevision<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
{
    /// <summary>The tenant.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>How many changes of rights the tenant has saved.</summary>
    public long Revision { get; set; }
}
