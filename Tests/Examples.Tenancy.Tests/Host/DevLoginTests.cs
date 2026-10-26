using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Auth.Supabase;
using DDDToolkit.Supporting.Tenancy;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// The dev login: tokens for the demonstration people, which the Supabase bearer takes like any other, and
/// nowhere but on a developer's machine.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DevLoginTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>The local stack's secret, as appsettings.Development.json has it.</summary>
    private const string LocalSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tokens_exist_only_for_demo_people()
    {
        var host = await sample.SharedAsync();
        using var client = host.CreateClient();

        var people = await client.GetFromJsonAsync<JsonElement>("/dev/people", Cancellation);
        people.EnumerateArray().Select(person => person.GetProperty("key").GetString())
            .Should().Equal("ada", "rhea", "leo", "juno", "vic", "seth", "tove", "hana", "maud");
        people.EnumerateArray().Select(person => person.GetProperty("email").GetString())
            .Should().OnlyContain(email => email!.EndsWith("@example.test", StringComparison.Ordinal));

        using (var rhea = await client.PostAsJsonAsync("/dev/auth/token", new { person = "rhea" }, Cancellation))
        {
            rhea.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var stranger = await client.PostAsJsonAsync("/dev/auth/token", new { person = "mallory" }, Cancellation))
        {
            (await stranger.ShouldBeRefusedAsync(HttpStatusCode.NotFound, DevLoginEndpoints.UnknownPerson)).Argument("Person").Should().Be("mallory");
        }

        using var bare = await client.GetAsync("/dev/token/mallory", Cancellation);
        await bare.ShouldBeRefusedAsync(HttpStatusCode.NotFound, DevLoginEndpoints.UnknownPerson);
    }

    [Fact]
    public async Task The_token_is_accepted_by_the_supabase_bearer()
    {
        var host = await sample.SharedAsync();
        using var client = host.CreateClient();
        using var signIn = await client.PostAsJsonAsync("/dev/auth/token", new { person = "rhea" }, Cancellation);
        var answer = await signIn.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var token = answer.GetProperty("access_token").GetString()!;

        // Supabase's shape, and Supabase's claims: nothing about the token says it came from the dev login.
        answer.GetProperty("token_type").GetString().Should().Be("bearer");
        answer.GetProperty("expires_in").GetInt32().Should().Be(3600);
        answer.GetProperty("user").GetProperty("id").GetGuid().Should().Be(DemoPeople.Rhea.Id);

        var jwt = new JsonWebToken(token);
        jwt.Alg.Should().Be("HS256");
        jwt.Issuer.Should().Be(SupabaseTokens.IssuerOf("http://127.0.0.1:54321"));
        jwt.Audiences.Should().Equal(SupabaseTokens.Audience);
        jwt.Subject.Should().Be(DemoPeople.Rhea.Id.ToString());
        jwt.GetClaim("role").Value.Should().Be("authenticated");
        jwt.GetClaim("email").Value.Should().Be("rhea@example.test");
        jwt.TryGetPayloadValue<object>("nbf", out _).Should().BeFalse("Supabase writes no nbf");
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromHours(1));
        foreach (var claim in new[] { "aal", "session_id", "is_anonymous", "app_metadata", "user_metadata" })
        {
            jwt.TryGetPayloadValue<object>(claim, out _).Should().BeTrue("a Supabase access token carries {0}", claim);
        }

        // The bearer takes it, and tenant selection finds Rhea's seat by its subject.
        using var mine = host.Client(token, tenant: null);
        var seats = await mine.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);
        seats.EnumerateArray().Select(seat => seat.GetProperty("tenant").GetProperty("slug").GetString()).Should().Equal("harbor");
    }

    [Fact]
    public async Task An_operator_signs_in_with_the_operators_role_and_without_a_seat()
    {
        var host = await sample.SharedAsync();
        using var client = host.CreateClient();
        using var signIn = await client.PostAsJsonAsync("/dev/auth/token", new { person = "orla" }, Cancellation);
        var answer = await signIn.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var token = answer.GetProperty("access_token").GetString()!;

        // The development settings list her as an operator, so her token claims the operators' role, as Supabase
        // Auth writes one for a member of staff; the bare token says the same.
        new JsonWebToken(token).GetClaim("role").Value.Should().Be(SampleTokenRoles.Operator);
        answer.GetProperty("user").GetProperty("role").GetString().Should().Be(SampleTokenRoles.Operator);
        new JsonWebToken(await client.GetStringAsync("/dev/token/orla", Cancellation)).GetClaim("role").Value.Should().Be(SampleTokenRoles.Operator);

        // She has no seat anywhere, and is not among the people a tenant has.
        using var orla = host.Client(token, tenant: null);
        (await orla.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation)).EnumerateArray().Should().BeEmpty();
        DemoPeople.All.Should().NotContain(DemoPeople.Orla);
        DemoData.Tenants.SelectMany(tenant => tenant.Seats.Prepend(tenant.Administrator)).Select(seat => seat.Person).Should().NotContain(DemoPeople.Orla);
    }

    [Fact]
    public async Task Who_is_an_operator_is_what_the_configuration_lists()
    {
        // Ada listed in Orla's place: an operator is whoever the identity provider says, here the settings.
        await using var host = await sample.StartAsync(settings: new Dictionary<string, string> { [DevOperators.Setting + ":0"] = "ada" });
        using var ada = await host.ClientAsync("ada", DemoData.Harbor.Slug);
        using var orla = await host.ClientAsync("orla", tenant: null);

        // Signed in as an operator, harbor's administrator reads across tenants and has no seat: a token is an
        // operator's or a seat's, never both.
        using (var tenants = await ada.GetAsync("/operations/tenants", Cancellation))
        {
            tenants.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var inHarbor = await ada.GetAsync("/me", Cancellation))
        {
            await inHarbor.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        }

        (await ada.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation)).EnumerateArray().Should().BeEmpty();

        // And a person nobody lists is a signed-in user like any other, without a seat: no operator.
        using var refused = await orla.GetAsync("/operations/tenants", Cancellation);
        await refused.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.OperatorsOnly);
    }

    [Fact]
    public async Task An_operator_nobody_knows_stops_the_host()
    {
        await using var host = await sample.NotStartedAsync(settings: new Dictionary<string, string> { [DevOperators.Setting + ":0"] = "mallory" });

        var start = () => host.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*DevLogin:Operators lists 'mallory'*");
    }

    [Fact]
    public async Task The_guard_refuses_dev_login_outside_development()
    {
        await using var production = await sample.NotStartedAsync(
            settings: new Dictionary<string, string>
            {
                ["DevLogin:Enabled"] = "true",
                ["Supabase:Url"] = "http://127.0.0.1:54321",
                ["Supabase:JwtSecret"] = LocalSecret,
            },
            environment: Environments.Production);

        var start = () => production.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*dev login*Production, not Development*");
    }

    [Theory]
    [InlineData("http://127.0.0.1:54321", "too-short-for-hs256", "*shorter than 32 bytes*")]
    [InlineData("http://127.0.0.1:54321", "", "*Supabase:JwtSecret is not set*")]
    [InlineData("https://tenancy.example.test", LocalSecret, "*Supabase:Url (https://tenancy.example.test) is not on this machine*")]
    public async Task The_guard_refuses_dev_login_with_a_weak_secret_or_a_remote_project(string url, string secret, string reason)
    {
        await using var development = await sample.NotStartedAsync(settings: new Dictionary<string, string>
        {
            ["DevLogin:Enabled"] = "true",
            ["Supabase:Url"] = url,
            ["Supabase:JwtSecret"] = secret,
        });

        var start = () => development.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage(reason);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Dev_routes_are_not_mapped_outside_development(string environment)
    {
        await using var host = await sample.StartAsync(
            settings: new Dictionary<string, string>
            {
                ["Supabase:Url"] = "http://127.0.0.1:54321",
                ["Supabase:JwtSecret"] = LocalSecret,
            },
            environment: environment);
        using var client = host.CreateClient();

        using var people = await client.GetAsync("/dev/people", Cancellation);
        using var token = await client.PostAsJsonAsync("/dev/auth/token", new { person = "rhea" }, Cancellation);
        using var bare = await client.GetAsync("/dev/token/rhea", Cancellation);
        using var attempts = await client.GetAsync("/dev/attempts", Cancellation);

        people.StatusCode.Should().Be(HttpStatusCode.NotFound);
        token.StatusCode.Should().Be(HttpStatusCode.NotFound);
        bare.StatusCode.Should().Be(HttpStatusCode.NotFound);
        attempts.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Services.GetService(typeof(LocalTokenIssuer)).Should().BeNull("nothing signs a token outside Development");
    }

    [Fact]
    public async Task A_remote_url_without_a_secret_starts_when_dev_login_is_off()
    {
        await using var host = await sample.StartAsync(settings: new Dictionary<string, string>
        {
            ["DevLogin:Enabled"] = "false",
            ["Supabase:Url"] = "https://tenancy.example.test",
            ["Supabase:JwtSecret"] = "",
        });
        using var client = host.CreateClient();

        using var health = await client.GetAsync("/health", Cancellation);
        using var seats = await client.GetAsync("/me/seats", Cancellation);
        using var people = await client.GetAsync("/dev/people", Cancellation);

        health.StatusCode.Should().Be(HttpStatusCode.OK);
        seats.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the bearer is there, and asks a real project's keys once a token arrives");
        people.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
