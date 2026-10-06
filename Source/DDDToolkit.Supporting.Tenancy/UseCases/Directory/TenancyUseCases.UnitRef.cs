namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>A unit, with its path from the root, such as <c>Harbor Works / North / North Coast</c>.</summary>
    public sealed record UnitRef(TUnitId Id, string Path);
}
