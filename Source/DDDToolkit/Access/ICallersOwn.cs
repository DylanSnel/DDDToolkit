using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// Says what the caller's own <typeparamref name="TValue"/> is: the handle a request about the caller's own goes through
/// (<see cref="AccessRequirement.Own{TValue}"/>, <see cref="OrOwnAttribute"/>, <see cref="OwnAttribute"/>). The core says
/// it for a <see cref="Guid"/>, the caller's verified identity, and Tenancy for its seat id, the caller's seat in the
/// tenant of the request. An application whose requests name a kind of its own, its own user id say, says it with a
/// small implementation of its own:
/// <code>
/// public sealed class CallersUser : ICallersOwn&lt;UserId&gt;
/// {
///     public UserId? Find(ICurrentUser user) =&gt; user.Id is { } id ? new UserId(id) : null;
/// }
///
/// services.AddCallersOwn&lt;UserId, CallersUser&gt;();
/// </code>
/// </summary>
/// <remarks>
/// Declare it beside the requests, in a project they reference or their own: the generator reads that it is there, and
/// reports <c>[OrOwn]</c> or <c>[Own]</c> on a type nothing says the caller's own of (DDD00086). It reads nothing, as
/// the check that asks it reads nothing: it answers from what is known of the caller already, the token's claims or what
/// the host resolved for the request, and is asked once per request that names a value of the type. A package that says
/// it for a type an application declares, as Tenancy does for the seat id, also says so for the generator with
/// <see cref="CallersOwnAttribute"/>.
/// <para>
/// It refuses nobody. A caller that has no such value, one who did not sign in, the application's own work, a caller
/// with no seat in the tenant, is answered <see langword="null"/>, and the request is not about its own.
/// </para>
/// </remarks>
/// <typeparam name="TValue">What a request names: the caller's identity, a seat, a user id of the application's.</typeparam>
public interface ICallersOwn<TValue>
    where TValue : struct, IEquatable<TValue>
{
    /// <summary>
    /// The value <paramref name="user"/> has of <typeparamref name="TValue"/>, or <see langword="null"/> when it has
    /// none.
    /// </summary>
    /// <param name="user">Who is calling.</param>
    TValue? Find(ICurrentUser user);
}
