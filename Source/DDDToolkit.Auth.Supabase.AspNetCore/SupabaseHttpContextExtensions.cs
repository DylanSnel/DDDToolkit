using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.AspNetCore.Http;

namespace DDDToolkit.Auth.Supabase.AspNetCore;

/// <summary>The caller a request came with, as the Supabase bearer scheme saw it.</summary>
public static class SupabaseHttpContextExtensions
{
    /// <summary>
    /// The request's own caller: the user of the Supabase access token the bearer scheme validated for it,
    /// or <see cref="Caller.Anonymous"/> without one. Never a caller begun with <see cref="Callers.Begin"/>,
    /// so a host can make the request's caller current at the start of a request even when another one
    /// leaked into it:
    /// <code>
    /// app.Use(async (context, next) =>
    /// {
    ///     using (Callers.Begin(context.GetSupabaseCaller()))
    ///     {
    ///         await next(context);
    ///     }
    /// });
    /// </code>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static Caller GetSupabaseCaller(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context is { User.Identity.IsAuthenticated: true } && context.Features.Get<SupabaseAccessTokenFeature>() is { } token
            ? token.Caller
            : Caller.Anonymous;
    }
}
