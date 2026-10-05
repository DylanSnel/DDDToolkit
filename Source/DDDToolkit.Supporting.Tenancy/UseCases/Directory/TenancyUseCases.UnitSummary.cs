namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A unit, with its path from the root and how deep it is, the root being 1: what the package keeps of a unit,
    /// and nothing the application added to its unit class. An application that shows a field of its own, such as
    /// what kind of unit it is, asks the directory with a view of the unit
    /// (<see cref="TenancyDirectory.ListUnitsAsync{TView}"/>, <see cref="TenancyDirectory.UnitsByIdAsync{TView}"/>),
    /// which hands it this summary and its own unit side by side.
    /// </summary>
    public sealed record UnitSummary(TUnitId Id, TUnitId? ParentId, string Name, UnitStatus Status, string Path, int Depth);
}
