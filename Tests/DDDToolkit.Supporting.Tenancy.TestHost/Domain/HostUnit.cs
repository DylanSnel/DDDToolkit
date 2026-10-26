using System.Text.RegularExpressions;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>The application's organization unit: the package's, with a cost centre and a rule about it.</summary>
[OrganizationUnit<OrganizationUnitId>]
public sealed partial class HostUnit
{
    /// <summary>The cost centre the unit books to, such as <c>NL-001</c>, or <see langword="null"/>.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>Sets or clears the cost centre.</summary>
    public void SetCostCentre(string? costCentre) => CostCentre = costCentre;

    /// <summary>A cost centre, when there is one, is two capitals, a dash and three digits.</summary>
    public sealed partial class CostCentreFormat : IInvariant<HostUnit>
    {
        /// <inheritdoc />
        public string Code => "host.unit.cost-centre";

        /// <inheritdoc />
        public InvariantFailure? Check(HostUnit entity)
            => entity.CostCentre is null || Format().IsMatch(entity.CostCentre)
                ? null
                : "A cost centre is two capitals, a dash and three digits.";

        [GeneratedRegex(@"^[A-Z]{2}-\d{3}$")]
        private static partial Regex Format();
    }
}
