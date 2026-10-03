namespace Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;

/// <summary>
/// The application's organization unit: the package's, with a cost centre and a rule about it.
/// </summary>
/// <remarks>
/// The package's rules about names, kinds, parents and archiving run first; <see cref="CostCentreFormat"/>
/// runs after them, on every save of the organization that holds the unit. That is how an application adds
/// a rule to a package's entity: next to the package's, never instead of one.
/// </remarks>
[OrganizationUnit<OrganizationUnitId>]
public sealed partial class OrganizationUnit
{
    /// <summary>The cost centre the unit books its work to, such as <c>NC-104</c>, or <see langword="null"/>.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>Sets or clears the cost centre. <see cref="CostCentreFormat"/> judges it when the organization is saved.</summary>
    public void SetCostCentre(string? costCentre) => CostCentre = costCentre;
}
