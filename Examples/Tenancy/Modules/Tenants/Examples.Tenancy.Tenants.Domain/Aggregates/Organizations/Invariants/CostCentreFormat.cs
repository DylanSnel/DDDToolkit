using System.Text.RegularExpressions;
using DDDToolkit.Invariants;

namespace Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;

// The unit's own rule, next to the package's: nested in the unit, as a partial of it, because the generator finds
// a class's rules among its own nested types.
public sealed partial class OrganizationUnit
{
    /// <summary>A cost centre, when there is one, is two capitals, a dash and three digits.</summary>
    public sealed partial class CostCentreFormat : IInvariant<OrganizationUnit>
    {
        /// <summary>The code this rule is reported by.</summary>
        public const string ViolationCode = "tenants.unit.cost-centre";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public InvariantFailure? Check(OrganizationUnit entity)
            => entity.CostCentre is null || Format().IsMatch(entity.CostCentre)
                ? null
                : "A cost centre is two capitals, a dash and three digits.";

        [GeneratedRegex(@"^[A-Z]{2}-\d{3}$")]
        private static partial Regex Format();
    }
}
