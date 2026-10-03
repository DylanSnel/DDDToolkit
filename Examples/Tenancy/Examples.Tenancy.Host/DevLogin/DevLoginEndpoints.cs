using System.Text.Json.Serialization;
using DDDToolkit.Auth.Supabase;
using DDDToolkit.Exceptions;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;

namespace Examples.Tenancy.Host.DevLogin;

/// <summary>
/// The dev login: sign in as any of the demonstration people with one click, and get the access token the
/// local Supabase stack would have issued them. Every route is anonymous, and none exists outside
/// Development with the dev login on (<see cref="Auth.DevLoginGuard"/>).
/// </summary>
/// <remarks>
/// The token is an ordinary Supabase access token. The rest of the API checks it with the Supabase bearer,
/// finds the person's seats by its <c>sub</c>, and cannot tell it from one issued by Supabase Auth: what the
/// demonstration shows about access is what a real sign-in gets.
/// <para>
/// A person the configuration lists as an operator (<see cref="DevOperators"/>) gets a token that claims the
/// operators' role, as Supabase Auth issues one to a member of the application's own staff. That person is not
/// among the cards of <c>GET /dev/people</c>, which are the people with a seat: an operator signs in with
/// <c>POST /dev/auth/token</c> or <c>GET /dev/token/{person}</c>.
/// </para>
/// </remarks>
public static class DevLoginEndpoints
{
    /// <summary>The code of a request for someone who is not one of the demonstration people.</summary>
    public const string UnknownPerson = "sample.unknown-person";

    /// <summary>
    /// Maps <c>GET /dev/people</c>, <c>POST /dev/auth/token</c>, <c>GET /dev/token/{person}</c> and
    /// <c>GET /dev/attempts</c>, when the environment is Development and the dev login is on and allowed;
    /// otherwise nothing.
    /// </summary>
    public static WebApplication MapDevLogin(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The issuer is registered only when the guard allowed the dev login, and the guard allows it only in
        // Development; asking both again costs nothing.
        if (!app.Environment.IsDevelopment() || app.Services.GetService<LocalTokenIssuer>() is null)
        {
            return app;
        }

        var dev = app.MapGroup("/dev").AllowAnonymous();
        var operators = DevOperators.From(app.Configuration);
        string RoleOf(DemoPerson person) => operators.Includes(person) ? SampleTokenRoles.Operator : LocalTokenIssuer.Role;

        // Who can be signed in as, for the login page's cards.
        dev.MapGet("/people", () => Results.Ok(DemoPeople.All.Select(person => new
        {
            person.Key,
            person.Id,
            person.Name,
            person.Email,
            person.About,
        })));

        // Supabase's own sign-in answers POST /auth/v1/token; this answers in the same shape, so a client written
        // for one reads the other. There is no refresh token: a demonstration signs in again.
        dev.MapPost("/auth/token", (TokenRequest body, LocalTokenIssuer issuer) =>
        {
            var person = Require(body.Person);
            var token = issuer.Issue(person, RoleOf(person));

            return Results.Ok(new TokenResponse(
                token.AccessToken,
                "bearer",
                (int)LocalTokenIssuer.Lifetime.TotalSeconds,
                token.ExpiresAt.ToUnixTimeSeconds(),
                new TokenUser(person.Id, SupabaseTokens.Audience, RoleOf(person), person.Email)));
        });

        // The bare token, for a terminal: curl -H "Authorization: Bearer $(curl -s .../dev/token/rhea)".
        dev.MapGet("/token/{person}", (string person, LocalTokenIssuer issuer) =>
        {
            var found = Require(person);
            return Results.Text(issuer.Issue(found, RoleOf(found)).AccessToken, "text/plain");
        });

        // The try-it presets, every id filled in from the demonstration data, each with the answer it should get.
        // They name people to sign in as, so they exist only where the dev login does.
        dev.MapGet("/attempts", () => Results.Ok(DevAttempts.All));

        return app;
    }

    private static DemoPerson Require(string? key)
        => DemoPeople.Find(key) ?? throw new RefusalException(
            UnknownPerson,
            RefusalKind.NotFound,
            "There is no demonstration person called '" + key + "'. Ask GET /dev/people who there is.",
            new Dictionary<string, object?> { ["Person"] = key });

    /// <summary>Whom to sign in as.</summary>
    /// <param name="Person">A demonstration person's key, such as <c>rhea</c>.</param>
    public sealed record TokenRequest(string? Person);

    /// <summary>The answer of Supabase Auth's token endpoint, as far as a client reads it.</summary>
    public sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("expires_at")] long ExpiresAt,
        [property: JsonPropertyName("user")] TokenUser User);

    /// <summary>The signed-in user, as Supabase Auth's token endpoint describes them.</summary>
    public sealed record TokenUser(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("aud")] string Audience,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("email")] string Email);
}
