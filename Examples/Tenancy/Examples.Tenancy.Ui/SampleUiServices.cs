using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Api.TryIt;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Languages;
using Examples.Tenancy.Ui.Session;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Examples.Tenancy.Ui;

/// <summary>What the UI registers: its session, its sign-in and its one client of the Host's API.</summary>
public static class SampleUiServices
{
    /// <summary>
    /// Where the API is when <c>Api:BaseUrl</c> does not say: the AppHost's resource <c>api</c>, found by service
    /// discovery, over HTTPS when it has an HTTPS endpoint and over HTTP otherwise.
    /// </summary>
    public const string ApiByServiceDiscovery = "https+http://api";

    /// <summary>
    /// Registers the circuit's <see cref="UiSession"/> and <see cref="SessionStore"/>, the pages' texts in the
    /// session's language (<see cref="UiTexts"/>), the dev login as the <see cref="ILoginClient"/>, the
    /// <see cref="AttemptRunner"/>, and <see cref="SampleApi"/> and <see cref="SupabaseLoginClient"/> as typed
    /// clients with no resilience handlers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The service defaults give every client a standard resilience handler, which retries a failed call. That is
    /// right for a service calling a service and wrong here: the try-it panel must show the API's first answer, and
    /// a retried POST would make a change twice. So this client has none.
    /// </para>
    /// <para>
    /// The session is scoped, which in Blazor Server means one per circuit, and <see cref="SampleApi"/> takes it in
    /// its constructor, so it is resolved in the circuit's scope with the circuit's session. A
    /// <see cref="DelegatingHandler"/> would not do: the client factory builds handlers in a scope of its own, where
    /// the circuit's session is out of reach.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSampleUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<UiSession>();
        services.AddScoped<SessionStore>();
        services.AddScoped<UiTexts>();
        services.AddScoped<ILoginClient, DevLoginClient>();
        services.AddScoped<AttemptRunner>();

        // RemoveAllResilienceHandlers is marked for evaluation (EXTEXP0001). It is the one way to take off a handler
        // that ConfigureHttpClientDefaults put on every client, and Ui/SampleApiTests fails if it stops working.
#pragma warning disable EXTEXP0001
        services
            .AddHttpClient<SampleApi>((provider, client) =>
                client.BaseAddress = new Uri(provider.GetRequiredService<IConfiguration>()["Api:BaseUrl"] is { Length: > 0 } configured
                    ? configured
                    : ApiByServiceDiscovery))
            .RemoveAllResilienceHandlers();

        // The sign-in with a password, at Supabase Auth itself. Without a setting that says where Auth is, the
        // client has no address, and the login page offers the dev login alone. It never retries either.
        services
            .AddHttpClient<SupabaseLoginClient>((provider, client) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                client.BaseAddress = SupabaseLoginClient.AuthAddressOf(configuration);
                if (configuration[SupabaseLoginClient.PublishableKeySetting] is { Length: > 0 } key)
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
                }
            })
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        // What the accept page does with the sign-in a link carries, per circuit: it may be waiting for its
        // person's answer.
        services.AddScoped<LinkSignIn>();

        return services;
    }
}
