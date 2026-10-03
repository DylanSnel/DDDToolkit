using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Languages;
using Examples.Tenancy.Ui.Session;

namespace Examples.Tenancy.Ui.Auth;

/// <summary>
/// Signs in at Supabase Auth with an e-mail address and a password, the way a real user of the application does:
/// <c>POST token?grant_type=password</c>. The answer carries the same kind of access token the dev login hands
/// out, so everything after the sign-in is the same for both.
/// </summary>
/// <remarks>
/// <para>
/// It runs on the server, in the circuit: the password goes from the form to Auth and is kept nowhere, and the
/// token stays in the circuit's session, as the dev login's does. Only the access token is kept; when it expires
/// the API answers 401 and the person signs in again, which is enough for a demonstration.
/// </para>
/// <para>
/// It is offered when the UI knows where Auth is: <see cref="AuthUrlSetting"/>, or a project's
/// <see cref="UrlSetting"/>, whose Auth answers under <c>/auth/v1/</c>. A project's gateway also asks for the
/// project's publishable key (<see cref="PublishableKeySetting"/>), which the registration sends as
/// <c>apikey</c>; an Auth server with nothing in front of it, as the AppHost runs one, asks for none.
/// </para>
/// <para>
/// A person who was invited has no password yet. The link in Auth's mail signs them in once, and the page it
/// leads to takes that sign-in (<see cref="SignInWithLinkAsync"/>) and has them choose a password
/// (<see cref="ChoosePasswordAsync"/>), so the next sign-in is the one above.
/// </para>
/// <para>
/// It never retries: its registration removes the resilience handlers the service defaults add, so a sign-in is
/// sent once.
/// </para>
/// <para>
/// What it tells a person who is refused is the UI's own text, in the language the session reads
/// (<see cref="UiTexts"/>): like <see cref="Api.SampleApi"/> it takes the circuit's session, and Auth's own words
/// are never shown.
/// </para>
/// </remarks>
/// <param name="http">A client whose base address is where Auth answers, or none when the sign-in is not offered.</param>
/// <param name="session">The circuit's session, for the language a refusal is told in.</param>
public sealed class SupabaseLoginClient(HttpClient http, UiSession session)
{
    /// <summary>Where Auth answers, when it is not under a project's URL.</summary>
    public const string AuthUrlSetting = "Supabase:AuthUrl";

    /// <summary>The Supabase project's URL.</summary>
    public const string UrlSetting = "Supabase:Url";

    /// <summary>The project's publishable key, which its gateway asks of every call.</summary>
    public const string PublishableKeySetting = "Supabase:PublishableKey";

    /// <summary>Whether there is an Auth server to sign in at.</summary>
    public bool IsOffered => http.BaseAddress is not null;

    /// <summary>
    /// Where Auth answers according to <paramref name="configuration"/>, with a trailing slash, or
    /// <see langword="null"/> when neither setting is there.
    /// </summary>
    public static Uri? AuthAddressOf(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var address = configuration[AuthUrlSetting] is { Length: > 0 } auth
            ? auth
            : configuration[UrlSetting] is { Length: > 0 } project ? project.TrimEnd('/') + "/auth/v1" : null;
        return address is null ? null : new Uri(address.TrimEnd('/') + "/");
    }

    /// <summary>
    /// An access token for the user with <paramref name="email"/> and <paramref name="password"/>, or why there
    /// is none. Neither the password nor the answer's tokens are put into the refusal. Auth answers the same for
    /// an address it does not know and for a wrong password, and so does this: one text for both.
    /// </summary>
    public async Task<PasswordSignIn> SignInAsync(string? email, string? password, CancellationToken cancellationToken = default)
    {
        if (!IsOffered)
        {
            return PasswordSignIn.RefusedBecause(UiTexts.Of("login.password.not-offered", session.Language));
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return PasswordSignIn.RefusedBecause(UiTexts.Of("login.password.incomplete", session.Language));
        }

        try
        {
            using var response = await http.PostAsJsonAsync("token?grant_type=password", new { email = email.Trim(), password }, cancellationToken);
            if ((int)response.StatusCode is 400 or 401 or 422)
            {
                return PasswordSignIn.RefusedBecause(UiTexts.Of("login.password.refused", session.Language));
            }

            if (!response.IsSuccessStatusCode)
            {
                return PasswordSignIn.RefusedBecause(string.Format(CultureInfo.InvariantCulture, UiTexts.Of("login.password.answered", session.Language), (int)response.StatusCode));
            }

            var answer = await response.Content.ReadFromJsonAsync<TokenAnswer>(JsonSerializerOptions.Web, cancellationToken);
            return answer is { AccessToken.Length: > 0 }
                ? new PasswordSignIn(answer, null)
                : PasswordSignIn.RefusedBecause(UiTexts.Of("login.password.no-token", session.Language));
        }
        catch (Exception exception) when (IsNoAnswer(exception, cancellationToken))
        {
            return PasswordSignIn.RefusedBecause(NoAnswer());
        }
    }

    /// <summary>
    /// The sign-in a link of Auth's mail gave, once Auth itself said whose it is: <c>GET user</c> with the
    /// link's access token. What an address carries proves nothing, so nothing of it starts a session before
    /// Auth has answered for it; the address of the person comes from that answer and not from the link.
    /// </summary>
    /// <param name="link">What the address carried.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <returns>The link's access token with the user Auth named, or why there is no sign-in.</returns>
    public async Task<PasswordSignIn> SignInWithLinkAsync(AuthLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!IsOffered)
        {
            return PasswordSignIn.RefusedBecause(UiTexts.Of("login.password.not-offered", session.Language));
        }

        if (link is not { AccessToken: { } accessToken, Expires: { } expires } || Bearer(accessToken) is not { } bearer)
        {
            return PasswordSignIn.RefusedBecause(UiTexts.Of("login.link.refused", session.Language));
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "user");
            request.Headers.Authorization = bearer;
            using var response = await http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is 400 or 401 or 403)
            {
                return PasswordSignIn.RefusedBecause(UiTexts.Of("login.link.refused", session.Language));
            }

            if (!response.IsSuccessStatusCode)
            {
                return PasswordSignIn.RefusedBecause(string.Format(CultureInfo.InvariantCulture, UiTexts.Of("login.password.answered", session.Language), (int)response.StatusCode));
            }

            var user = await response.Content.ReadFromJsonAsync<TokenUser>(JsonSerializerOptions.Web, cancellationToken);
            return user is { Email.Length: > 0 }
                ? new PasswordSignIn(new TokenAnswer(accessToken, expires.ToUnixTimeSeconds(), user), null)
                : PasswordSignIn.RefusedBecause(UiTexts.Of("login.link.refused", session.Language));
        }
        catch (Exception exception) when (IsNoAnswer(exception, cancellationToken))
        {
            return PasswordSignIn.RefusedBecause(NoAnswer());
        }
    }

    /// <summary>
    /// Gives whoever <paramref name="accessToken"/> signs in the password they chose: Auth's own call for
    /// changing one's user, <c>PUT user</c>. The password goes to Auth and is kept nowhere; what Auth asks of a
    /// password, its length and the like, is Auth's to say.
    /// </summary>
    /// <param name="accessToken">The access token of the person's own sign-in.</param>
    /// <param name="password">The password they chose.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <returns><see langword="null"/> when the password is set, or what to tell the person, in the session's language.</returns>
    public async Task<string?> ChoosePasswordAsync(string? accessToken, string? password, CancellationToken cancellationToken = default)
    {
        if (!IsOffered)
        {
            return UiTexts.Of("login.password.not-offered", session.Language);
        }

        if (string.IsNullOrEmpty(password))
        {
            return UiTexts.Of("login.password.not-taken", session.Language);
        }

        if (Bearer(accessToken) is not { } bearer)
        {
            return UiTexts.Of("login.password.session-over", session.Language);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "user") { Content = JsonContent.Create(new { password }) };
            request.Headers.Authorization = bearer;
            using var response = await http.SendAsync(request, cancellationToken);
            return (int)response.StatusCode switch
            {
                >= 200 and <= 299 => null,
                401 or 403 => UiTexts.Of("login.password.session-over", session.Language),
                400 or 422 => UiTexts.Of("login.password.not-taken", session.Language),
                var status => string.Format(CultureInfo.InvariantCulture, UiTexts.Of("login.password.answered", session.Language), status),
            };
        }
        catch (Exception exception) when (IsNoAnswer(exception, cancellationToken))
        {
            return NoAnswer();
        }
    }

    /// <summary>The header for an access token, or <see langword="null"/> for a text no header can carry: one that came off an address may be anything.</summary>
    private static AuthenticationHeaderValue? Bearer(string? accessToken)
    {
        try
        {
            return string.IsNullOrWhiteSpace(accessToken) ? null : new AuthenticationHeaderValue("Bearer", accessToken);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsNoAnswer(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException or JsonException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private string NoAnswer()
        => string.Format(CultureInfo.InvariantCulture, UiTexts.Of("login.password.no-answer", session.Language), AuthUrlSetting);
}
