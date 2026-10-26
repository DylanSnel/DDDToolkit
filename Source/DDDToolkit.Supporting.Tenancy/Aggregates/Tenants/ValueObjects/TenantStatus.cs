namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Where a tenant is in its life. It moves one way: provisioned, then active, suspended and active again as
/// often as needed, and closed once, for good.
/// </summary>
public enum TenantStatus
{
    /// <summary>Being set up; nobody works in it yet.</summary>
    Provisioning,

    /// <summary>In use.</summary>
    Active,

    /// <summary>Stopped for now, with a reason, and able to be reactivated.</summary>
    Suspended,

    /// <summary>Stopped for good, with a reason. Nothing changes a closed tenant.</summary>
    Closed,
}
