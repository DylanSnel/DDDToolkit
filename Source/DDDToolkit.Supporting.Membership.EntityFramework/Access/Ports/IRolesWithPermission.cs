using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Which of the roles an application keeps give a permission, for one kind of resource whose rules say its roles are
/// kept elsewhere (<see cref="Roles.External"/>). An application implements it over the
/// rows it keeps its roles in, and registers it with the resource.
/// <para>
/// It answers a query, not a list: the access questions put it into their own statement, where it becomes a
/// subquery next to the member rows, so what a caller holds stays one statement. It is therefore asked with
/// the context that statement runs on, and answers over rows that context maps, the application's own or the
/// ones another module lets it read:
/// <code>
/// public IQueryable&lt;DutyId&gt; RolesWith(DbContext context, Caller caller, string permission)
///     =&gt; context.Set&lt;DutyPermission&gt;().Where(given =&gt; given.Permission == permission).Select(given =&gt; given.DutyId);
/// </code>
/// </para>
/// <para>
/// The questions of one request may be asked side by side, each on a context of its own, so it keeps nothing
/// between two questions and reads on no context but the one it is handed.
/// </para>
/// <para>
/// What a role holds here is not all it gives a member: the package asks only for a permission within the rules'
/// <see cref="MembershipRules.RolePermissions"/>, so a role that holds a permission outside it gives nothing with that
/// permission on the resource, whatever this answers.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public interface IRolesWithPermission<TResourceId, TRoleId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>
    /// The roles that give <paramref name="permission"/> now: those in use, as <paramref name="caller"/> has them,
    /// in its part of an organization say. Not run here: it goes into the statement that asks.
    /// </summary>
    /// <param name="context">The context the statement runs on: the query is over rows of it.</param>
    /// <param name="caller">The caller the question is asked for: a signed-in user.</param>
    /// <param name="permission">A permission a member's role can give on the resource.</param>
    IQueryable<TRoleId> RolesWith(DbContext context, Caller caller, string permission);
}
