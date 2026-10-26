using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Where a caller holds a key, for one kind of resource that is reached from above
/// (<see cref="MembershipRules.Above"/>): the places a hold of the key reaches, which a resource that sits at
/// one of them is then held on. An application implements it over whatever keeps who holds what where, the
/// units of an organization say, and registers it with the resource.
/// <para>
/// It answers a query, not a list: the access questions put it into their own statement, where it becomes a
/// subquery next to the resource's row, so what a caller holds stays one statement. It is therefore asked
/// with the context that statement runs on, and answers over rows that context maps:
/// <code>
/// public IQueryable&lt;BayId&gt; PlacesReached(DbContext context, Caller caller, string key)
/// {
///     var user = caller.UserId;
///     return from porter in context.Set&lt;Porter&gt;()
///            where porter.UserId == user &amp;&amp; porter.Active
///            join hold in context.Set&lt;BayHold&gt;() on porter.Id equals hold.PorterId
///            where hold.Key == key
///            join path in context.Set&lt;BayPath&gt;() on hold.BayId equals path.AboveId
///            select path.BayId;
/// }
/// </code>
/// </para>
/// <para>
/// The questions of one request may be asked side by side, each on a context of its own, so it keeps nothing
/// between two questions and reads on no context but the one it is handed.
/// </para>
/// <para>
/// It is asked about every signed-in caller, a member of the resource or not, and also about one the
/// application answered nobody for as a member (<see cref="ICallerMember{TResourceId, TMemberId}.Find"/>):
/// reach from above is for callers that are on no member list. So whether the caller still counts is asked
/// here as well, of the same rows or the same fact the application answers who a member is from. An
/// application that answers from the caller's id alone lets somebody it no longer counts keep what they held.
/// </para>
/// <para>
/// What is above what is the application's to know: the package compares where a resource sits
/// (<c>HasMembers(..., at: ...)</c>) with what this answers, and keeps no places and no tree of them. A key
/// answered here is held whatever the rules say a member's role gives: that list is about members.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource that sits somewhere.</typeparam>
/// <typeparam name="TPlaceId">What a place is known by: what the resource's row stores where it sits.</typeparam>
public interface IPlacesReached<TResourceId, TPlaceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TPlaceId : struct, IEntityId, IEquatable<TPlaceId>
{
    /// <summary>
    /// The places <paramref name="caller"/>'s hold of <paramref name="key"/> reaches now: each place the key
    /// is held at, and every place below it. None for a caller that holds the key nowhere. Not run here: it
    /// goes into the statement that asks.
    /// </summary>
    /// <param name="context">The context the statement runs on: the query is over rows of it.</param>
    /// <param name="caller">The caller the question is asked for: a signed-in user.</param>
    /// <param name="key">A permission key.</param>
    IQueryable<TPlaceId> PlacesReached(DbContext context, Caller caller, string key);
}
