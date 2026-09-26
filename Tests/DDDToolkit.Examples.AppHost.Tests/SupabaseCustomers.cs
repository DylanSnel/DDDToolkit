using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DDDToolkit.Examples.AppHost.Tests;

/// <summary>
/// Signed-in customers of the Supabase monolith: access tokens as Supabase Auth issues them.
/// <list type="bullet">
/// <item>Against the AppHost's container, signed here with the Supabase CLI's local JWT secret, which is
/// what the AppHost gives the host to check them with.</item>
/// <item>Against a real project (<c>ConnectionStrings__Supabase</c> set), from the project's own Auth:
/// each customer is created with the Auth admin API, signs in with a password, and is deleted again
/// afterwards. That needs <c>Supabase__Url</c> and <c>SUPABASE_SECRET_KEY</c>; without them the
/// scenarios that need a customer skip, saying so.</item>
/// </list>
/// </summary>
public sealed class SupabaseCustomers : IAsyncDisposable
{
    /// <summary>What <c>supabase start</c> uses, and the AppHost passes to the host with its container.</summary>
    private const string LocalUrl = "http://127.0.0.1:54321";

    private const string LocalJwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    private readonly HttpClient? _auth;
    private readonly List<string> _created = [];

    private SupabaseCustomers(HttpClient? auth, string? unavailable)
    {
        _auth = auth;
        Unavailable = unavailable;
    }

    /// <summary>Why there are no customers to be had, or <see langword="null"/> when there are.</summary>
    public string? Unavailable { get; }

    /// <summary>Customers for the sample as the environment runs it: against the container, or against a project.</summary>
    public static SupabaseCustomers ForThisRun()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Supabase")))
        {
            return new(auth: null, unavailable: null);
        }

        var url = Environment.GetEnvironmentVariable("Supabase__Url");
        var secret = Environment.GetEnvironmentVariable("SUPABASE_SECRET_KEY");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(secret))
        {
            return new(auth: null, unavailable: "Against a Supabase project, customers are made with its Auth admin API, which needs Supabase__Url and SUPABASE_SECRET_KEY.");
        }

        var auth = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/auth/v1/") };
        auth.DefaultRequestHeaders.Add("apikey", secret);
        auth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return new(auth, unavailable: null);
    }

    /// <summary>A new customer's access token.</summary>
    public async Task<string> SignInAsync(string name, CancellationToken cancellationToken)
    {
        if (Unavailable is not null)
        {
            Assert.Skip(Unavailable);
        }

        var email = $"ddd-{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        return _auth is null
            ? LocalToken(Guid.NewGuid(), email)
            : await ProjectTokenAsync(email, cancellationToken);
    }

    private async Task<string> ProjectTokenAsync(string email, CancellationToken cancellationToken)
    {
        var auth = _auth ?? throw new InvalidOperationException("There is no project to sign in to.");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        using (var created = await auth.PostAsJsonAsync("admin/users", new { email, password, email_confirm = true }, cancellationToken))
        {
            created.EnsureSuccessStatusCode();
            var user = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            _created.Add(user.GetProperty("id").GetString()!);
        }

        using var signedIn = await auth.PostAsJsonAsync("token?grant_type=password", new { email, password }, cancellationToken);
        signedIn.EnsureSuccessStatusCode();
        return (await signedIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("access_token").GetString()!;
    }

    /// <summary>An access token as the local stack signs one: HS256 with its JWT secret.</summary>
    private static string LocalToken(Guid user, string email)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = LocalUrl + "/auth/v1",
            ["aud"] = "authenticated",
            ["sub"] = user.ToString(),
            ["role"] = "authenticated",
            ["email"] = email,
            ["iat"] = now,
            ["exp"] = now + 3600,
        }));

        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(LocalJwtSecret), Encoding.ASCII.GetBytes($"{header}.{payload}"));
        return $"{header}.{payload}.{Encode(signature)}";
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async ValueTask DisposeAsync()
    {
        if (_auth is null)
        {
            return;
        }

        foreach (var id in _created)
        {
            using var deleted = await _auth.DeleteAsync($"admin/users/{id}");
        }

        _auth.Dispose();
    }
}
