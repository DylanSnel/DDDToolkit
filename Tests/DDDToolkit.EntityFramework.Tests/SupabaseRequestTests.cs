using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DDDToolkit.Auth.Supabase;
using DDDToolkit.Auth.Supabase.AspNetCore;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The whole way from a request to a policy: an ASP.NET Core application on Kestrel validates a Supabase
/// access token, and the notes it reads for that request are the ones Postgres's row level security
/// lets that user see. The tokens are signed with the Supabase CLI's local JWT secret, the way the
/// local stack signs them.
/// </summary>
public sealed class SupabaseRequestTests(SupabaseRowLevelSecurityDatabase database)
    : IClassFixture<SupabaseRowLevelSecurityDatabase>, IAsyncLifetime
{
    private const string ProjectUrl = "http://127.0.0.1:54321";

    /// <summary>What <c>supabase status</c> prints as the JWT secret of a local stack.</summary>
    private const string JwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private WebApplication? _app;
    private HttpClient? _client;

    private HttpClient Client => _client ?? throw new InvalidOperationException("The application did not start.");

    public async ValueTask InitializeAsync()
    {
        if (!database.Available)
        {
            return;
        }

        await database.ClearAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddAuthentication().AddSupabaseJwtBearer(ProjectUrl, jwt => jwt.UseSupabaseJwtSecret(JwtSecret));
        builder.Services.AddAuthorization();
        builder.Services.AddSupabaseRowLevelSecurity(options => options.SystemRole = SupabaseRowLevelSecurity.ServiceRole);
        builder.Services.AddDbContext<NotesContext>((provider, options) => options
            .UseNpgsql(database.ApplicationConnectionString)
            .UseSupabaseRowLevelSecurity(provider));

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapPost("/notes", async (NewNote body, NotesContext notes, CancellationToken cancellationToken) =>
        {
            var note = new PrivateNote { Id = Guid.CreateVersion7(), Text = body.Text };
            notes.Notes.Add(note);
            await notes.SaveChangesAsync(cancellationToken);
            return Results.Created($"/notes/{note.Id}", new { note.Id, note.Owner });
        });

        _app.MapGet("/notes/{id:guid}", async (Guid id, NotesContext notes, CancellationToken cancellationToken)
            => await notes.Notes.SingleOrDefaultAsync(note => note.Id == id, cancellationToken) is { } note
                ? Results.Ok(new { note.Text, note.Owner })
                : Results.NotFound());

        _app.MapGet("/claims", async (NotesContext notes, CancellationToken cancellationToken)
            => Results.Text(await notes.Database.SqlQueryRaw<string>("""SELECT coalesce(auth.jwt()::text, 'null') AS "Value" """).SingleAsync(cancellationToken)));

        // A step a request takes on the application's behalf, on purpose: a caller begun in code wins.
        _app.MapGet("/notes/count-as-the-system", async (NotesContext notes, CancellationToken cancellationToken) =>
        {
            using (Callers.Begin(Caller.System))
            {
                return Results.Ok(await notes.Notes.CountAsync(cancellationToken));
            }
        });

        await _app.StartAsync(Cancellation);

        var address = _app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_user_reads_their_own_note_and_nobody_else_can()
    {
        database.Require();

        using var created = await SendAsync(HttpMethod.Post, "/notes", Token(Alice), new NewNote("Alice's"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var note = await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        note.GetProperty("owner").GetGuid().Should().Be(Alice, "the database filled the owner in from the token, with auth.uid()");
        var path = created.Headers.Location!.OriginalString;

        using (var asAlice = await SendAsync(HttpMethod.Get, path, Token(Alice)))
        {
            asAlice.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var asBob = await SendAsync(HttpMethod.Get, path, Token(Bob)))
        {
            asBob.StatusCode.Should().Be(HttpStatusCode.NotFound, "Bob's query ran as Bob, and the policy hid Alice's note from it");
        }

        using (var asNobody = await SendAsync(HttpMethod.Get, path, token: null))
        {
            asNobody.StatusCode.Should().Be(HttpStatusCode.NotFound, "a request without a token runs as anon");
        }
    }

    [Fact]
    public async Task A_token_that_does_not_validate_makes_nobody_a_user()
    {
        database.Require();

        using var created = await SendAsync(HttpMethod.Post, "/notes", Token(Alice), new NewNote("Alice's"));
        var path = created.Headers.Location!.OriginalString;

        using (var forged = await SendAsync(HttpMethod.Get, path, Token(Alice, secret: "somebody-else's-secret-that-is-also-32-characters-long")))
        {
            forged.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var expired = await SendAsync(HttpMethod.Get, path, Token(Alice, expires: DateTime.UtcNow.AddMinutes(-10))))
        {
            expired.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var otherProject = await SendAsync(HttpMethod.Get, path, Token(Alice, issuer: "https://someone-else.supabase.co/auth/v1")))
        {
            otherProject.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Postgres_gets_the_claims_the_token_was_signed_with()
    {
        database.Require();

        using var response = await SendAsync(HttpMethod.Get, "/claims", Token(Alice));
        using var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));

        claims.RootElement.GetProperty("sub").GetString().Should().Be(Alice.ToString());
        claims.RootElement.GetProperty("email").GetString().Should().Be("alice@example.com");
        claims.RootElement.GetProperty("app_metadata").GetProperty("teams")[0].GetString().Should().Be("north", "nested claims arrive nested, as PostgREST would pass them");
    }

    [Fact]
    public async Task Work_outside_a_request_runs_as_the_system_role()
    {
        database.Require();

        (await SendAsync(HttpMethod.Post, "/notes", Token(Alice), new NewNote("Alice's"))).Dispose();
        (await SendAsync(HttpMethod.Post, "/notes", Token(Bob), new NewNote("Bob's"))).Dispose();

        // An outbox poller, a hosted service: a scope of its own and no request.
        await using var scope = _app!.Services.CreateAsyncScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesContext>();

        (await notes.Notes.CountAsync(Cancellation)).Should().Be(2, "background work runs as service_role here, which row level security does not apply to");
    }

    [Fact]
    public async Task A_caller_begun_in_code_wins_over_the_request()
    {
        database.Require();

        (await SendAsync(HttpMethod.Post, "/notes", Token(Alice), new NewNote("Alice's"))).Dispose();
        (await SendAsync(HttpMethod.Post, "/notes", Token(Bob), new NewNote("Bob's"))).Dispose();

        using var response = await SendAsync(HttpMethod.Get, "/notes/count-as-the-system", Token(Alice));

        (await response.Content.ReadFromJsonAsync<int>(Cancellation)).Should().Be(2, "Alice's request counted as the system, which it began on purpose");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request, Cancellation);
    }

    /// <summary>An access token as Supabase Auth signs one for <paramref name="user"/>.</summary>
    private static string Token(Guid user, string secret = JwtSecret, string issuer = ProjectUrl + "/auth/v1", DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = SupabaseTokens.Audience,
            IssuedAt = (expires ?? now.AddHours(1)).AddHours(-1),
            NotBefore = (expires ?? now.AddHours(1)).AddHours(-1),
            Expires = expires ?? now.AddHours(1),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.ToString(),
                ["role"] = "authenticated",
                ["email"] = user == Alice ? "alice@example.com" : "bob@example.com",
                ["app_metadata"] = new Dictionary<string, object> { ["provider"] = "email", ["teams"] = new[] { "north" } },
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256),
        });
    }

    private sealed record NewNote(string Text);
}
