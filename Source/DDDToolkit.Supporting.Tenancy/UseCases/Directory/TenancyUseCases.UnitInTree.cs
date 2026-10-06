namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A unit, the application's own and whole, with where it sits in the tree: its path from the root and how deep
    /// it is. The unit carries its id, its parent, its name, its status and every field the application added, such
    /// as what kind of unit it is; the path and the depth are not on it. They come from the closure and the units
    /// above it, which the answer does not carry, as a seat lists only the units under its placements, so the
    /// application would need a read more to work them out. So that is all this adds, and an application shows a
    /// unit by what it selects (<see cref="TenancyDirectory.ListUnitsAsync"/>, <see cref="TenancyDirectory.UnitsByIdAsync"/>).
    /// </summary>
    /// <remarks>
    /// The unit was read for this answer and is tracked by nobody: nothing done to it is saved, by this question or by
    /// a save later in the same unit of work.
    /// </remarks>
    /// <param name="Unit">The unit, the application's own class.</param>
    /// <param name="Path">The names of the unit and the units above it, the root first, joined with <c>" / "</c>.</param>
    /// <param name="Depth">How deep the unit is, the root being 1.</param>
    public sealed record UnitInTree(TUnit Unit, string Path, int Depth);
}
