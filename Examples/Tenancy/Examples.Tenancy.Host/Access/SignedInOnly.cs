using Microsoft.AspNetCore.Authentication;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// Lets only a signed-in caller through: a request without a valid token is challenged, a 401 as on every other
/// route.
/// </summary>
/// <remarks>
/// For what is not an endpoint. A route asks for a token with <c>RequireAuthorization()</c>; the GraphQL gateway
/// is a branch of the pipeline and has no endpoint to put that on, so the host puts this on the gateway's path,
/// before the gateway. Authentication has run by then, so the request's user is the token's.
/// </remarks>
public static class SignedInOnly
{
    /// <summary>Challenges a request whose user is not authenticated, and passes every other on.</summary>
    /// <param name="app">The pipeline, or the branch of it, to guard.</param>
    public static IApplicationBuilder UseSignedInOnly(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                await context.ChallengeAsync();
                return;
            }

            await next(context);
        });
    }
}
