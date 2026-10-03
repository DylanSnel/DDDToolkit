using System.Text.Json;
using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Auth;

namespace Examples.Tenancy.Ui.Api.TryIt;

/// <summary>
/// Runs the try-it presets exactly as they say: as their person, with or without that person's token, with or
/// without the <c>Tenant</c> header, with their body as it is.
/// </summary>
/// <remarks>
/// The person's token comes from the login client, and the call carries it through <see cref="CallAs.Person"/>, so
/// the session is never touched: a preset that runs as vic leaves the UI signed in as whoever it was, and a preset
/// that earns a 401 does not sign anybody out.
/// </remarks>
public sealed class AttemptRunner(SampleApi api, ILoginClient login)
{
    /// <summary>Runs <paramref name="attempt"/> and returns its answer.</summary>
    public async Task<AttemptRun> RunAsync(AttemptPreset attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var caller = CallAs.Anonymous;
        if (attempt.SendToken)
        {
            var signedIn = await login.SignInAsync(attempt.Person, cancellationToken);
            if (signedIn.Value is not { } token)
            {
                // The preset never ran: show why, which is the sign-in's own answer.
                return new AttemptRun(attempt, signedIn);
            }

            caller = CallAs.Person(attempt.Person, token.AccessToken);
        }

        var tenant = attempt.SendTenant ? TenantChoice.Of(attempt.Tenant) : TenantChoice.None;
        var outcome = await api.SendAsync<JsonElement?>(new HttpMethod(attempt.Method), attempt.Path, attempt.Body, caller, tenant, cancellationToken);
        return new AttemptRun(attempt, outcome);
    }
}
