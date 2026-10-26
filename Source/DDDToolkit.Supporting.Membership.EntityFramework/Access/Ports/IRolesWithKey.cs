using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Which of the roles an application keeps give a key, for one kind of resource whose rules say its roles are
/// kept elsewhere (<see cref="MembershipRules.RolesKeptElsewhere"/>). An application implements it over the
/// rows it keeps its roles in, and registers it with the resource.
/// <para>
/// It answers a query, not a list: the access questions put it into their own statement, where it becomes a
/// subquery next to the member rows, so what a caller holds stays one statement. It is therefore asked with
/// the context that statement runs on, and answers over rows that context maps, the application's own or the
/// ones another module lets it read:
/// <code>
/// public IQueryable&lt;DutyId&gt; RolesWith(DbContext context, Caller caller, string key)
///     =&gt; context.Set&lt;DutyKey&gt;().Where(given =&gt; given.Key == key).Select(given =&gt; given.DutyId);
/// </code>
/// </para>
/// <para>
/// The questions of one request may be asked side by side, each on a context of its own, so it keeps nothing
/// between two questions and reads on no context but the one it is handed.
/// </para>
/// <para>
/// What a role holds here is not all it gives a member: the package asks only for a key within the rules'
/// <see cref="MembershipRules.MemberKeys"/>, so a role that holds a key outside it gives nothing with that
/// key on the resource, whatever this answers.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public interface IRolesWithKey<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>
    /// The roles that give <paramref name="key"/> now: those in use, as <paramref name="caller"/> has them,
    /// in its part of an organization say. Not run here: it goes into the statement that asks.
    /// </summary>
    /// <param name="context">The context the statement runs on: the query is over rows of it.</param>
    /// <param name="caller">The caller the question is asked for: a signed-in user.</param>
    /// <param name="key">A permission key a member's role can give on the resource.</param>
    IQueryable<TRoleId> RolesWith(DbContext context, Caller caller, string key);
}
