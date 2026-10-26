namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// Whether a role kept for a resource is in use. An archived role gives nothing, on whichever member holds
/// it, and is not given again.
/// </summary>
public enum KeptRoleStatus
{
    /// <summary>In use.</summary>
    Active,

    /// <summary>No longer in use. It stays, so the members that hold it still hold something that has a name.</summary>
    Archived,
}
