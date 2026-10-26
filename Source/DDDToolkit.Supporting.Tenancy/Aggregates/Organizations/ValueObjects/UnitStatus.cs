namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Whether a unit is in use. A unit is archived, never deleted, so what still points at it keeps a unit to
/// point at.
/// </summary>
public enum UnitStatus
{
    /// <summary>In use: units can be added below it and seats placed in it.</summary>
    Active,

    /// <summary>No longer in use. What already hangs at it keeps working; nothing new is added to it.</summary>
    Archived,
}
