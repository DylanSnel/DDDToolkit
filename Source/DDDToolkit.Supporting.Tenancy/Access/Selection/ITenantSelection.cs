using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// <see cref="TenantSelection{TTenantId, TSeatId}"/> without its id types, for the code that makes a request's
/// Tenancy caller current and needs no id to do it. A host's middleware takes the caller it is answered and begins
/// it, and <see cref="TenancyCallers.Begin"/> takes any <see cref="ITenancyCaller"/>, so the middleware names
/// neither of the application's ids, and reads the same in every application:
/// <code>
/// var seat = await context.RequestServices.GetRequiredService&lt;ITenantSelection&gt;()
///     .ResolveAsync(caller, context.Request.Headers["Tenant"], context.RequestAborted);
/// using (TenancyCallers.Begin(seat))
/// {
///     await next(context);
/// }
/// </code>
/// <para>
/// <c>AddTenancy</c> registers it per scope as the very selection it registers closed over the ids, so the two
/// answer alike, unless the host registered one of its own before, which then stays. What answers records with the
/// ids in them, a person's own seats for a tenant picker
/// (<see cref="TenantSelection{TTenantId, TSeatId}.SeatsOfAsync"/>), is that one's alone.
/// </para>
/// </summary>
public interface ITenantSelection
{
    /// <summary>
    /// The Tenancy caller for <paramref name="caller"/> in the tenant <paramref name="tenantSlug"/> names: its active
    /// seat there, or nobody and why, as <see cref="TenantSelection{TTenantId, TSeatId}.ResolveAsync"/> answers it.
    /// Every refusal comes back as nobody, with the refusal's code; nothing is thrown for a caller Tenancy does not
    /// let in.
    /// </summary>
    /// <param name="caller">Who is calling, as the toolkit says.</param>
    /// <param name="tenantSlug">The tenant the request names, by its slug; trimmed and lowercased.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<ITenancyCaller> ResolveAsync(Caller caller, string? tenantSlug, CancellationToken cancellationToken);
}
