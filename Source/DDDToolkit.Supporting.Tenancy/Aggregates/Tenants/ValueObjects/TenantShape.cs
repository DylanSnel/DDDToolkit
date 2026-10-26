namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Whether a tenant's organization is a tree or only its root. A tenant starts flat or hierarchical, and a
/// flat one can become hierarchical; the way back is closed, because units below the root would have to go.
/// </summary>
public enum TenantShape
{
    /// <summary>The organization is its root alone, and every seat is placed there.</summary>
    Flat,

    /// <summary>The organization is a tree of units below the root.</summary>
    Hierarchical,
}
