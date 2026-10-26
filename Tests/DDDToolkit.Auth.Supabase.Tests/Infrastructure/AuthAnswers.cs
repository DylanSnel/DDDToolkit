using System.Text.Json;

namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// Answers in the shapes Supabase Auth gives them, taken from an Auth server (2.196) and filled with this
/// suite's own values. The user is the whole document, with the address in three places and an identity
/// that has a sign-in time of its own, so a test also proves what the client leaves unread.
/// </summary>
internal static class AuthAnswers
{
    /// <summary>The text Auth gives an address that has an account, in both shapes of its errors.</summary>
    public const string AddressExistsText = "A user with this email address has already been registered";

    /// <summary>A user as Auth answers one to the admin API.</summary>
    /// <param name="id">The user's id.</param>
    /// <param name="address">The user's address.</param>
    /// <param name="invitedAt">When Auth mailed an invitation, if it did.</param>
    /// <param name="lastSignInAt">When the user last signed in, if ever.</param>
    /// <param name="emailConfirmedAt">When the address was proven, if it was.</param>
    public static string User(Guid id, string address, string? invitedAt = null, string? lastSignInAt = null, string? emailConfirmedAt = null)
    {
        var user = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["aud"] = "authenticated",
            ["role"] = "authenticated",
            ["email"] = address,
            ["phone"] = "",
            ["app_metadata"] = new { provider = "email", providers = new[] { "email" } },
            ["user_metadata"] = new { },
            ["identities"] = new[]
            {
                new
                {
                    identity_id = Guid.Parse("862af6a6-f973-46b7-8e9d-125f2e05e3dc"),
                    id,
                    user_id = id,
                    identity_data = new { email = address, email_verified = false, phone_verified = false, sub = id },
                    provider = "email",
                    // Auth sets this when it makes the identity, whether or not anyone ever signed in.
                    last_sign_in_at = "2026-03-02T09:15:54.006149897Z",
                    created_at = "2026-03-02T09:15:54.006202Z",
                    updated_at = "2026-03-02T09:15:54.006202Z",
                    email = address,
                },
            },
            ["created_at"] = "2026-03-02T09:15:53.993957Z",
            ["updated_at"] = "2026-03-02T09:15:54.045591Z",
            ["is_anonymous"] = false,
        };

        if (invitedAt is not null)
        {
            user["invited_at"] = invitedAt;
            user["confirmation_sent_at"] = invitedAt;
        }

        if (lastSignInAt is not null)
        {
            user["last_sign_in_at"] = lastSignInAt;
        }

        if (emailConfirmedAt is not null)
        {
            user["email_confirmed_at"] = emailConfirmedAt;
            user["confirmed_at"] = emailConfirmedAt;
        }

        return JsonSerializer.Serialize(user);
    }

    /// <summary>A refusal in the shape Auth gives by default: the status as <c>code</c>, the code as <c>error_code</c>.</summary>
    public static string Refusal(int status, string code, string text)
        => JsonSerializer.Serialize(new { code = status, error_code = code, msg = text });

    /// <summary>A refusal in the shape Auth gives a client that asks for its newer API version: the code as <c>code</c>.</summary>
    public static string NewerRefusal(string code, string text)
        => JsonSerializer.Serialize(new { code, message = text });

    /// <summary>What Auth answers when the database refuses, such as an id that is taken: the database's own code and texts.</summary>
    public static string DatabaseRefusal(string code, string text, string detail)
        => JsonSerializer.Serialize(new { code, message = text, detail });
}
