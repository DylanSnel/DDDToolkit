using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Auth.Supabase.AspNetCore;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// The tenant a request works in: named by its slug in the <c>Tenant</c> header, and resolved against the
/// caller's own seats on every request.
/// </summary>
/// <remarks>
/// The header only names a tenant; it never picks a seat. The tenant's id and the seat's id come from the seat
/// directory, which looks up the verified identity in the caller's token, so a person who names a tenant they
/// have no seat in is nobody there, exactly as for a tenant that does not exist. A person with seats in two
/// tenants switches between them by sending the other slug.
/// </remarks>
public static class TenantHeader
{
    /// <summary>The header that carries the tenant's slug.</summary>
    public const string Name = "Tenant";

    /// <summary>
    /// Makes the rest of the request run as the request's own caller, the user of its validated token or
    /// anonymous, and as the Tenancy caller that caller resolves to: its active seat in the tenant the header
    /// names, or nobody and why. Add it after authentication, so the token is validated, and before authorization,
    /// which asks for the seat.
    /// </summary>
    /// <remarks>
    /// It always resolves, and never refuses: whether a route needs a seat is the route's business
    /// (<see cref="SamplePolicies.SeatRequired"/>), and a route that does not, such as a person's own
    /// list of seats or the health check, runs as nobody without harm.
    /// <para>
    /// Both callers are begun for the request, not asked for: the toolkit's from the token
    /// (<see cref="SupabaseHttpContextExtensions.GetSupabaseCaller"/>), never from whatever is ambient, and
    /// Tenancy's from the seat that caller has. A caller that is somehow current when a request starts, such as
    /// system work begun around a call and never ended, is replaced for the request rather than inherited, and a
    /// warning says so: a request is never system work. The seat is looked up with the leaked Tenancy caller
    /// already replaced by nobody, so the lookup does not run in the leaked tenant either. The toolkit's
    /// accessors answer a begun caller first, so the request's queries run as its own user.
    /// </para>
    /// <para>
    /// It names no id. The selection is asked without its id types (<see cref="ITenantSelection"/>), and what it
    /// answers is begun as it is, so this middleware reads the same in every application, whatever its ids.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseTenantSelection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TenantHeader));

        return app.Use(async (context, next) =>
        {
            if (Callers.Ambient is { } leakedCaller)
            {
                logger.LogWarning(
                    "A {Kind} caller was current when the request {Method} {Path} started; the request runs as its own instead.",
                    leakedCaller.Kind,
                    context.Request.Method,
                    context.Request.Path);
            }

            var leaked = TenancyCallers.Ambient;
            if (leaked is not null)
            {
                logger.LogWarning(
                    "A {Kind} Tenancy caller was current when the request {Method} {Path} started; the request resolves its own instead.",
                    leaked.Kind,
                    context.Request.Method,
                    context.Request.Path);
            }

            var caller = context.GetSupabaseCaller();
            using (Callers.Begin(caller))
            {
                ITenancyCaller? seat;
                using (leaked is null ? null : TenancyCallers.BeginNone())
                {
                    seat = await ResolveAsync(context, caller, logger);
                }

                // A lookup that failed leaves the request with no Tenancy caller at all, which every reader takes
                // for nobody, not seated: never with the leaked one.
                using (seat is null ? TenancyCallers.BeginNone() : TenancyCallers.Begin(seat))
                {
                    await next(context);
                }
            }
        });
    }

    /// <summary>
    /// The Tenancy caller for this request: its seat, or nobody and why. A lookup that fails answers
    /// <see langword="null"/>, and the request runs as nobody rather than failing here: a route that needs a seat
    /// then refuses it the usual way, and one that does not is unaffected.
    /// </summary>
    private static async Task<ITenancyCaller?> ResolveAsync(HttpContext context, Caller caller, ILogger logger)
    {
        var slug = context.Request.Headers[Name].ToString();
        try
        {
            var selection = context.RequestServices.GetRequiredService<ITenantSelection>();
            return await selection.ResolveAsync(caller, slug, context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(exception, "The tenant {Slug} could not be resolved for {Method} {Path}; the request runs as nobody.", slug, context.Request.Method, context.Request.Path);
            return null;
        }
    }
}
