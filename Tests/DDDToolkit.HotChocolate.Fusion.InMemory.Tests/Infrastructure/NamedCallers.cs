using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

/// <summary>
/// Who calls, for an application under test: <c>Authorization: Name ada</c> signs a request in as ada, a request
/// without the header is anonymous, and a request that fails a policy is challenged or forbidden as with any token.
/// </summary>
internal static class NamedCallers
{
    public const string Scheme = "Name";

    /// <summary>The policy only ada passes: what an administration's endpoint asks.</summary>
    public const string Administrators = "Administrators";

    /// <summary>Authentication by name, and authorization with <see cref="Administrators"/>.</summary>
    public static IServiceCollection AddNamedCallers(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, Handler>(Scheme, configureOptions: null);
        services.AddAuthorizationBuilder().AddPolicy(Administrators, policy => policy.RequireAuthenticatedUser().RequireClaim(ClaimTypes.Name, "ada"));
        return services;
    }

    /// <summary>Signs the request in as <paramref name="name"/>.</summary>
    public static void As(this HttpRequestMessage request, string name) => request.Headers.Authorization = new AuthenticationHeaderValue(Scheme, name);

    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
                || header.Scheme != NamedCallers.Scheme
                || string.IsNullOrEmpty(header.Parameter))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var caller = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, header.Parameter)], NamedCallers.Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(caller, NamedCallers.Scheme)));
        }
    }
}
