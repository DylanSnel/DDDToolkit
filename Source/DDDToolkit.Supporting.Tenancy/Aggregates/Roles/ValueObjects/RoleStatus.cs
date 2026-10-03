namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Whether a role is in use. An archived role grants nothing, wherever it is still granted, and is not
/// granted again.
/// </summary>
public enum RoleStatus
{
    /// <summary>In use.</summary>
    Active,

    /// <summary>No longer in use. It stays, so the grants that name it still name something.</summary>
    Archived,
}
