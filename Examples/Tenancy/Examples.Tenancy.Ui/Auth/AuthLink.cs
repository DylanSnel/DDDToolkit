using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace Examples.Tenancy.Ui.Auth;

/// <summary>
/// What the part after the <c>#</c> of an address holds when a person arrives on the page that accepts an
/// invitation: the invitation's token, and behind it the sign-in Supabase Auth adds when the person came by the
/// link in its mail.
/// </summary>
/// <remarks>
/// <para>
/// A browser sends that part of an address to no server, which is why both travel there: neither is in a request
/// line, a log or a referrer. The page reads it in the circuit and takes it off the address at once.
/// </para>
/// <para>
/// The address Auth was told to send the person on to ends in <c>#</c> and the invitation's token, and Auth adds
/// its own part behind a <c>#</c> of its own: <c>token#access_token=…&amp;expires_at=…&amp;type=invite</c>. A
/// link whoever invited handed over has the token alone. A link Auth did not take, because it was used before or
/// ran out, arrives with <c>error=…&amp;error_code=…</c> and nothing else. Auth's part is names with values, and a
/// token has no <c>=</c>, which is how the two are told apart.
/// </para>
/// <para>
/// Everything here is what an address said, so none of it is trusted: the token is checked by the API when it is
/// used, and the sign-in by Auth before the session starts (<see cref="SupabaseLoginClient.SignInWithLinkAsync"/>).
/// Auth's refresh token is not read: the UI keeps an access token until it expires, and no more.
/// </para>
/// </remarks>
/// <param name="Token">The invitation's token, or <see langword="null"/> when the address carries none.</param>
/// <param name="AccessToken">The access token Auth signed the person in with, or <see langword="null"/>.</param>
/// <param name="Expires">When that access token expires, as Auth said.</param>
/// <param name="Kind">What kind of link Auth followed, such as <c>invite</c>.</param>
/// <param name="Refusal">Auth's code for why it did not take the link, such as <c>otp_expired</c>, or <see langword="null"/>.</param>
public sealed record AuthLink(string? Token, string? AccessToken, DateTimeOffset? Expires, string? Kind, string? Refusal)
{
    /// <summary>The kind of the link in the mail Auth sends an account that was made for an invited address.</summary>
    public const string Invitation = "invite";

    /// <summary>Whether the address carried anything at all.</summary>
    public bool IsEmpty => Token is null && AccessToken is null && Refusal is null;

    /// <summary>What <paramref name="address"/> carries after its <c>#</c>.</summary>
    /// <param name="address">The address as the browser has it, with its fragment.</param>
    public static AuthLink Read(string? address)
    {
        var hash = address?.IndexOf('#') ?? -1;
        var fragment = hash < 0 ? "" : address![(hash + 1)..];

        // The application's part first, then what Auth added behind a '#' of its own.
        var second = fragment.IndexOf('#');
        var (token, added) = second >= 0 ? (fragment[..second], fragment[(second + 1)..])
            : fragment.Contains('=') ? ("", fragment)
            : (fragment, "");

        var values = QueryHelpers.ParseQuery(added);
        string? Value(string name) => values.TryGetValue(name, out var found) && found.ToString() is { Length: > 0 } value ? value : null;

        return new AuthLink(
            token.Trim() is { Length: > 0 } carried ? carried : null,
            Value("access_token"),
            long.TryParse(Value("expires_at"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds < DateTimeOffset.MaxValue.ToUnixTimeSeconds()
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null,
            Value("type"),
            Value("error_code") ?? Value("error"));
    }

    /// <summary>Says that it is a link and nothing of what it carries: a record prints its members, and these are keys.</summary>
    public override string ToString() => nameof(AuthLink);
}
