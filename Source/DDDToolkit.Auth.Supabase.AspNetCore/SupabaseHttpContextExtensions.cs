using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.AspNetCore.Http;

namespace DDDToolkit.Auth.Supabase.AspNetCore;

/// <summary>The user a request came with, as the Supabase bearer scheme saw it.</summary>
public static class SupabaseHttpContextExtensions
{
    /// <summary>
    /// The request's own user: the user of the Supabase access token the bearer scheme validated for it, or
    /// <see cref="CurrentUserSnapshot.Anonymous"/> without one. It is never a user begun with
    /// <see cref="AccessManager.BeginAs(CurrentUserSnapshot)"/>. So a host can make the request's user current at the
    /// start of a request, even when another user leaked into it:
    /// <code>
    /// app.Use(async (context, next) =>
    /// {
    ///     var access = context.RequestServices.GetRequiredService&lt;AccessManager&gt;();
    ///     using (access.BeginAs(context.GetSupabaseUser()))
    ///     {
    ///         await next(context);
    ///     }
    /// });
    /// </code>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static CurrentUserSnapshot GetSupabaseUser(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context is { User.Identity.IsAuthenticated: true } && context.Features.Get<SupabaseAccessTokenFeature>() is { } token
            ? token.User
            : CurrentUserSnapshot.Anonymous;
    }
}
