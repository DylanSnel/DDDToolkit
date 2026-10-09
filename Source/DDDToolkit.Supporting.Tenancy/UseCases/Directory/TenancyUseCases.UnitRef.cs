namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A unit, with its path from the root, such as <c>Harbor Works / North / North Coast</c>.</summary>
    /// <param name="Id">The unit.</param>
    /// <param name="Path">The names of the unit and the units above it, the root first, joined with <c>" / "</c>.</param>
    public sealed record UnitRef(TUnitId Id, string Path);
}
