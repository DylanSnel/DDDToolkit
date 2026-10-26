using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

// What the database lets the application's own staff read of the projects.
// Tenancy's contribution writes the operators' role a policy on every table a module keeps to a tenant that lets
// it write nothing, whatever a rule says, and read only what a rule of the module admits. Without this rule an
// operator would read no project at all.

/// <summary>
/// The application's operators read every project, in every tenant, with its crew. They add, change and remove
/// none: the rule is for reading only, and Tenancy's own policies for the operators' role refuse every write
/// besides.
/// </summary>
[RowAccess<Project>(RowOperations.Read, To = [RowAccessRoles.TokenPrefix + SampleTokenRoles.Operator])]
public static partial class OperatorsSeeEveryProject
{
    /// <summary>Whether an operator may read <paramref name="project"/>: every one of them.</summary>
    public static bool Allows(Project project, Caller caller) => true;
}
