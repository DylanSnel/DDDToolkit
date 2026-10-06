using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

// The application's ids. The tenant's is a long, to prove the package forces no Guid on anyone: it says how a new one
// is made, as every id over anything but a Guid does, and the others get theirs from the generator.

/// <summary>A tenant's id, and its organization's.</summary>
[EntityId<long>]
public readonly partial record struct TenantId
{
    /// <summary>The last number <see cref="Create"/> handed out: a million, clear of the small ids tests write by hand.</summary>
    private static long _last = 1_000_000;

    /// <summary>
    /// A new tenant id, which Tenancy asks for when it provisions a tenant: the next number of this process. That is
    /// a test host's way, whose databases live no longer than the process. An application makes a long no other
    /// process makes either: a snowflake, or a number of a block a HiLo sequence of its database hands out.
    /// </summary>
    public static TenantId Create() => new(Interlocked.Increment(ref _last));
}

/// <summary>A seat's id.</summary>
[EntityId<Guid>]
public readonly partial record struct SeatId;

/// <summary>An organization unit's id.</summary>
[EntityId<Guid>]
public readonly partial record struct OrganizationUnitId;

/// <summary>A role's id.</summary>
[EntityId<Guid>]
public readonly partial record struct RoleId;

/// <summary>An invitation's id.</summary>
[EntityId<Guid>]
public readonly partial record struct InvitationId;
