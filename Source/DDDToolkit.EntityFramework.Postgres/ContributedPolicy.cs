using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// A policy an <see cref="IRowAccessContribution"/> writes on a table of the context it answered for, for one
/// command and one role. It is named like a rule's policy, <c>"&lt;Name&gt; (select) for authenticated"</c>, and
/// carries the rules' comment, so the context's next script drops it and makes it again.
/// </summary>
/// <remarks>
/// A permissive policy is merged with the rules' and other contributions' permissive policies for the same
/// table, command and role into one, whose conditions are OR-ed, as Postgres would OR them anyway. A
/// permissive policy for <c>ALL</c> commands counts as one for each. A restrictive policy is never merged:
/// it is written for its command as it is, <c>ALL</c> included, and narrows whatever the permissive ones
/// allow. Two policies of one name on a table are refused.
/// </remarks>
/// <param name="Table">A table-mapped entity type of the context; its table gets row level security.</param>
/// <param name="Name">What the policy is about, which its name starts with.</param>
/// <param name="Command"><c>SELECT</c>, <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c> or <c>ALL</c>.</param>
/// <param name="Role">The role it is for: a symbolic one, such as <c>RowAccessRoles.User</c>, or a role's own name. Never <c>PUBLIC</c>.</param>
/// <param name="Using">
/// Which existing rows it lets the role see, change or remove: for every command but <c>INSERT</c>, which has
/// none. It may ask functions by <c>{fn:owner/name}</c> and the caller by <c>{caller:...}</c>.
/// </param>
/// <param name="WithCheck">
/// Which rows it lets the role leave behind: for <c>INSERT</c>, and for <c>UPDATE</c> and <c>ALL</c>, where it
/// is <paramref name="Using"/> when left out; never for <c>SELECT</c> or <c>DELETE</c>.
/// </param>
/// <param name="Restrictive">Whether it narrows what the permissive policies allow, rather than allowing more.</param>
public sealed record ContributedPolicy(
    IEntityType Table,
    string Name,
    string Command,
    string Role,
    string? Using,
    string? WithCheck,
    bool Restrictive = false)
{
    /// <summary>
    /// Set when the policy is a default that a rule of the application may take the place of: what the role reads
    /// of the table, until the application says otherwise with a <c>[RowAccess]</c> rule that allows it
    /// <c>Read</c>. <see langword="null"/> for a policy no rule replaces, which is every policy a contribution does
    /// not mark. See <see cref="ContributedDefault"/>.
    /// </summary>
    public ContributedDefault? Default { get; init; }
}
