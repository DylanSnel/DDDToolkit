using Examples.Tenancy.Ui.Api.Wire;

namespace Examples.Tenancy.Ui.Auth;

/// <summary>
/// How a sign-in at Supabase Auth ended, with a password or with the link of a mail: with a token, or with what
/// to tell the person.
/// </summary>
/// <param name="Answer">Auth's answer, with the access token; <see langword="null"/> when it refused.</param>
/// <param name="Refusal">What to show when there is no token, in the session's language: never the password, and never a part of a token.</param>
public sealed record PasswordSignIn(TokenAnswer? Answer, string? Refusal)
{
    /// <summary>A sign-in that gave no token, with <paramref name="why"/> to show.</summary>
    public static PasswordSignIn RefusedBecause(string why) => new(null, why);
}
