using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Signing in as the demonstration people, the way the UI does it: through the dev login, which answers with
/// an ordinary Supabase access token.
/// </summary>
public static class SamplePeople
{
    /// <summary>An access token for <paramref name="person"/>, from <c>POST /dev/auth/token</c>.</summary>
    public static async Task<string> SignInAsync(this SampleFactory factory, string person)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/dev/auth/token", new { person }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// A client that calls as <paramref name="person"/>, with their token, in the tenant
    /// <paramref name="tenant"/> names: no <c>Tenant</c> header when it is <see langword="null"/>.
    /// </summary>
    public static async Task<HttpClient> ClientAsync(this SampleFactory factory, string person, string? tenant)
        => factory.Client(await factory.SignInAsync(person), tenant);

    /// <summary>A client that sends <paramref name="token"/>, when there is one, and the <c>Tenant</c> header, when there is one.</summary>
    public static HttpClient Client(this SampleFactory factory, string? token, string? tenant)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add(TenantHeader.Name, tenant);
        }

        return client;
    }
}
