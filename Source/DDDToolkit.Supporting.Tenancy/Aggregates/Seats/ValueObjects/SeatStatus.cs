namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Whether a seat's grants count. Only an active seat holds rights; a suspended one can be reactivated,
/// and a deactivated one is final.
/// </summary>
public enum SeatStatus
{
    /// <summary>In use: its grants give it rights.</summary>
    Active,

    /// <summary>Stopped for now. Its placements and grants stay, and give it nothing until it is reactivated.</summary>
    Suspended,

    /// <summary>Stopped for good.</summary>
    Deactivated,
}
