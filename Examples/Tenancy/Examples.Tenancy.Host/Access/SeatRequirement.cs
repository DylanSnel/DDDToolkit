using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.AspNetCore.Authorization;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// What every route that works inside a tenant requires: the request resolved to an active seat in the tenant it
/// named (<see cref="TenantHeader.UseTenantSelection"/>).
/// </summary>
/// <remarks>
/// This is a courtesy, not the protection. Every use case and every access question asks the Tenancy caller again
/// and refuses nobody on its own, so a route that forgot the policy would still refuse; the policy only gives
/// every route the same answer before any work starts. It lives in the host, so the modules' endpoints never name
/// it: a module maps its routes into whatever group the host hands it.
/// <para>
/// Authorization runs before a route's arguments are bound. So a caller without a seat is answered about the
/// seat first: a malformed id or body from such a caller is a 403 <c>tenancy.not-seated</c>, and never the 400
/// <c>invalid-request</c> a seated caller gets for the same body.
/// </para>
/// </remarks>
public sealed class SeatRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// The refusal <paramref name="caller"/> is answered with, or <see langword="null"/> for a seat. A request that
    /// named no tenant is a 400 with <c>tenancy.tenant-required</c>; any other caller who is nobody is a 403 with
    /// the reason tenant selection gave, such as <c>tenancy.not-seated</c> or <c>tenancy.seat-suspended</c>.
    /// </summary>
    /// <remarks>
    /// One function, so whatever else guards an entrance of the host asks the same question. It reads what kind of
    /// caller it is and why nobody is nobody, which a caller says without its ids, so it takes one as
    /// <see cref="ITenancyCaller"/>: the guards pass <c>TenancyUseCases.CurrentCaller()</c>.
    /// </remarks>
    /// <param name="caller">The request's Tenancy caller.</param>
    public static RefusalException? RefusalFor(ITenancyCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Kind is TenancyCallerKind.Seat)
        {
            return null;
        }

        // A request is never system work: tenant selection replaces any caller it finds. So anything but a seat is
        // nobody here, and nobody says why.
        var refusal = TenancyRefusals.Of(caller.Refusal ?? TenancyRefusals.NotSeated);
        return refusal.Code == TenancyRefusals.TenantRequired
            ? refusal
            : new RefusalException(refusal.Code, RefusalKind.NotPermitted, refusal.Message, refusal.Arguments);
    }
}
