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
    /// Makes the rest of the request run as the request's own user, the user of its validated token or anonymous,
    /// and as the Tenancy user that user resolves to: its active seat in the tenant the header names, or nobody and
    /// why. Add it after authentication, so the token is validated, and before authorization, which asks for the seat.
    /// </summary>
    /// <remarks>
    /// It always resolves, and never refuses: whether a route needs a seat is the route's business
    /// (<see cref="SamplePolicies.SeatRequired"/>), and a route that does not, such as a person's own
    /// list of seats or the health check, runs as nobody without harm.
    /// <para>
    /// Both are begun for the request, not asked for. The toolkit's current user comes from the token
    /// (<see cref="SupabaseHttpContextExtensions.GetSupabaseUser"/>), never from whatever was begun before. Tenancy's
    /// caller comes from the seat that user has. A user that is somehow begun when a request starts, such as system
    /// work begun around a call and never ended, is replaced for the request rather than inherited, and a warning says
    /// so: a request is never system work. The seat is looked up with the leaked Tenancy user already replaced by
    /// nobody, so the lookup does not run in the leaked tenant either. <see cref="ICurrentUser"/> answers a begun user
    /// first, so the request's queries run as its own user. The core's <see cref="AccessManager"/> begins it: the
    /// middleware is made once for the application, and the access manager is a singleton, as the current user is.
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
        var access = app.ApplicationServices.GetRequiredService<AccessManager>();
        var currentUser = access.CurrentUser;

        return app.Use(async (context, next) =>
        {
            if (currentUser.IsBegun)
            {
                logger.LogWarning(
                    "A {Kind} user was begun when the request {Method} {Path} started; the request runs as its own instead.",
                    currentUser.Kind,
                    context.Request.Method,
                    context.Request.Path);
            }

            var leaked = TenancyUsers.Ambient;
            if (leaked is not null)
            {
                logger.LogWarning(
                    "A {Kind} Tenancy user was current when the request {Method} {Path} started; the request resolves its own instead.",
                    leaked.Kind,
                    context.Request.Method,
                    context.Request.Path);
            }

            var requestUser = context.GetSupabaseUser();
            using (access.BeginAs(requestUser))
            {
                ITenancyUser? seat;
                using (leaked is null ? null : TenancyUsers.BeginNone())
                {
                    seat = await ResolveAsync(context, requestUser, logger);
                }

                // A lookup that failed leaves the request with no Tenancy user at all, which every reader takes
                // for nobody, not seated: never with the leaked one.
                using (seat is null ? TenancyUsers.BeginNone() : TenancyUsers.Begin(seat))
                {
                    await next(context);
                }
            }
        });
    }

    /// <summary>
    /// The Tenancy user for this request: its seat, or nobody and why. A lookup that fails answers
    /// <see langword="null"/>, and the request runs as nobody rather than failing here: a route that needs a seat
    /// then refuses it the usual way, and one that does not is unaffected.
    /// </summary>
    private static async Task<ITenancyUser?> ResolveAsync(HttpContext context, CurrentUserSnapshot user, ILogger logger)
    {
        var slug = context.Request.Headers[Name].ToString();
        try
        {
            var selection = context.RequestServices.GetRequiredService<ITenantSelection>();
            return await selection.ResolveAsync(user, slug, context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(exception, "The tenant {Slug} could not be resolved for {Method} {Path}; the request runs as nobody.", slug, context.Request.Method, context.Request.Path);
            return null;
        }
    }
}
