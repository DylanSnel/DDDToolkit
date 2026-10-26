using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.TestHost;

/// <summary>
/// What the caller's own <see cref="UserId"/> is: the id its token names, the same one a document's members are known
/// by. The host's handle for the requests about the caller's own, <c>[OrOwn] UserId Member</c>, which it registers with
/// <c>services.AddCallersOwn&lt;UserId, CallersUser&gt;()</c>: the core says the caller's own of a <see cref="Guid"/>, and
/// a kind of the application's own is the application's to say.
/// </summary>
public sealed class CallersUser : ICallersOwn<UserId>
{
    /// <inheritdoc />
    public UserId? Find(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return caller.UserId is { } id ? new UserId(id) : null;
    }
}
