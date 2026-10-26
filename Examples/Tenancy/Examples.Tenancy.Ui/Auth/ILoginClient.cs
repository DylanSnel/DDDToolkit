using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Api;

namespace Examples.Tenancy.Ui.Auth;

/// <summary>
/// How the UI gets an access token for a person. The login page and the try-it presets only ever ask this, so the
/// way a token is obtained can change without either of them changing.
/// </summary>
/// <remarks>
/// It is the Host's dev login (<see cref="DevLoginClient"/>): pick a demonstration person, no password. Beside it,
/// where the UI knows a Supabase Auth server, <see cref="SupabaseLoginClient"/> signs a person in with their
/// e-mail address and password. Both end with the same kind of token, and the API cannot tell the two apart.
/// </remarks>
public interface ILoginClient
{
    /// <summary>The people who can be signed in as without a password; empty where there are none.</summary>
    Task<ApiOutcome<IReadOnlyList<PersonCard>>> PeopleAsync(CancellationToken cancellationToken = default);

    /// <summary>An access token for <paramref name="person"/>, or the refusal that explains why there is none.</summary>
    Task<ApiOutcome<TokenAnswer>> SignInAsync(string person, CancellationToken cancellationToken = default);
}
