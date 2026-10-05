using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;

namespace Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;

/// <summary>
/// The application's organization unit: the package's, with what kind of unit it is, a cost centre and a rule
/// about the cost centre.
/// </summary>
/// <remarks>
/// The package's rules about names, parents and archiving run first; <see cref="CostCentreFormat"/> runs after
/// them, on every save of the organization that holds the unit. That is how an application adds a rule to a
/// package's entity: next to the package's, never instead of one.
/// <para>
/// The kind is this class's own, as the cost centre is: the package keeps none, since no access rule reads one. The
/// use case that makes a unit hands it to a callback before the save, and whoever calls it sets the kind there: the
/// module's command <c>AddOrganizationUnit</c> in the <c>configure</c> of <c>AddUnitAsync</c>, and whoever
/// provisions a tenant, in this sample the host's demo seeder, the root's in <c>ConfigureRoot</c>.
/// </para>
/// </remarks>
[OrganizationUnit<OrganizationUnitId>]
public sealed partial class OrganizationUnit
{
    /// <summary>What kind of unit it is, or <see langword="null"/> when whoever added it did not say.</summary>
    public UnitKind? Kind { get; private set; }

    /// <summary>The cost centre the unit books its work to, such as <c>NC-104</c>, or <see langword="null"/>.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>Says what kind of unit it is, or clears the kind.</summary>
    public void SetKind(UnitKind? kind) => Kind = kind;

    /// <summary>Sets or clears the cost centre. <see cref="CostCentreFormat"/> judges it when the organization is saved.</summary>
    public void SetCostCentre(string? costCentre) => CostCentre = costCentre;
}
