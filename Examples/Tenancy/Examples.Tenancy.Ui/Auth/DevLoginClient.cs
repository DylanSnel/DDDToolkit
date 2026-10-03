using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Api;

namespace Examples.Tenancy.Ui.Auth;

/// <summary>
/// Signs in through the Host's dev login: <c>GET /dev/people</c> for whom, <c>POST /dev/auth/token</c> for the
/// token. The token is an ordinary Supabase access token, signed with the local stack's secret, so everything the
/// UI then does goes through the same bearer and the same tenant selection a real sign-in would.
/// </summary>
/// <remarks>
/// The dev login exists only when the Host runs in Development with it switched on. Anywhere else these calls
/// answer 404, and the login page says so.
/// </remarks>
public sealed class DevLoginClient(SampleApi api) : ILoginClient
{
    /// <inheritdoc />
    public Task<ApiOutcome<IReadOnlyList<PersonCard>>> PeopleAsync(CancellationToken cancellationToken = default)
        => api.PeopleAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ApiOutcome<TokenAnswer>> SignInAsync(string person, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        return api.TokenAsync(person, cancellationToken);
    }
}
