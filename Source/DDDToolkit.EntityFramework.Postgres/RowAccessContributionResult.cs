using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// What an <see cref="IRowAccessContribution"/> writes for one context: its functions, which the script
/// writes with the access functions and in the order they ask each other; its policies, which the script
/// writes with the rules'; and statements of its own, written after both, such as triggers and the functions
/// they run.
/// </summary>
/// <param name="Functions">The SQL functions, each created in the schema of the context.</param>
/// <param name="Policies">The policies, each on a table of the context.</param>
/// <param name="Statements">
/// SQL that can run again, one statement each, after the functions and the policies: triggers and their
/// functions. A statement that makes a <c>SECURITY DEFINER</c> function says <c>SET search_path</c> in that
/// same statement, or the script refuses it.
/// </param>
/// <param name="ExclusiveTables">
/// Tables whose policies only this contribution writes: a <c>[RowAccess]</c> rule or another contribution
/// that would add one to them is refused. Each gets row level security, with or without a policy.
/// </param>
public sealed record RowAccessContributionResult(
    IReadOnlyList<ContributedFunction> Functions,
    IReadOnlyList<ContributedPolicy> Policies,
    IReadOnlyList<string> Statements,
    IReadOnlyList<IEntityType>? ExclusiveTables = null);
