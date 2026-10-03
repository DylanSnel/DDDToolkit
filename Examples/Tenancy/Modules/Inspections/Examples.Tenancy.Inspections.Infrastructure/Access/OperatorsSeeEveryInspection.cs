using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;

namespace Examples.Tenancy.Inspections.Infrastructure.Access;

/// <summary>
/// The application's operators read every inspection, in every tenant. They record none: the rule is for reading
/// only, and Tenancy's own policies for the operators' role refuse every write besides. Without this rule an
/// operator would read no inspection at all.
/// </summary>
[RowAccess<Inspection>(RowOperations.Read, To = [RowAccessRoles.TokenPrefix + SampleTokenRoles.Operator])]
public static partial class OperatorsSeeEveryInspection
{
    /// <summary>Whether an operator may read <paramref name="inspection"/>: every one of them.</summary>
    public static bool Allows(Inspection inspection, Caller caller) => true;
}
