using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Auth.Supabase.AspNetCore;
using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// A token Supabase's own Auth server signed, checked by the bearer scheme and by the validator. The stack
/// gives Auth a signing key, as Supabase's own local stack gives its Auth one and as a hosted project with
/// signing keys has, so the token is signed with that key and can only be checked with what Auth publishes.
/// </summary>
/// <remarks>
/// What a stand-in cannot say: how Auth signs once it has a key, what it names in the token's header, and
/// what exactly it publishes. <see cref="SupabaseTokenHandlerTests"/> and <see cref="SupabaseJwtBearerTests"/>
/// hold the rules against keys the tests publish themselves, without Docker.
/// </remarks>
public sealed class SupabaseBearerOnAuthTests(SupabaseAuthStack stack)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_person_who_signs_in_at_auth_gets_a_token_signed_with_the_key_auth_publishes_and_a_host_takes_it()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var address = $"signs-in-{Guid.NewGuid():N}@example.test";
        var password = "Pw-" + Guid.NewGuid().ToString("N");
        var person = await admin.CreateUserAsync(new SupabaseNewUser(address, Password: password, EmailConfirmed: true), Cancellation);

        var token = await auth.AccessTokenAsync(address, password, Cancellation);

        // What Auth signed: for this project, with its signing key and not with its secret.
        var jwt = new JsonWebToken(token);
        jwt.Alg.Should().Be(SecurityAlgorithms.EcdsaSha256);
        jwt.Issuer.Should().Be(SupabaseTokens.IssuerOf(SupabaseAuthStack.ProjectUrl));
        jwt.Subject.Should().Be(person.Id.ToString());

        // What Auth publishes: the public half of that key, and nothing else.
        using var anyone = new HttpClient();
        var published = await anyone.GetFromJsonAsync<JsonElement>(SupabaseTokens.KeysAddressOf(auth.Url.ToString()), Cancellation);
        var key = published.GetProperty("keys").EnumerateArray().Should().ContainSingle("Auth publishes its signing key and never its secret").Subject;
        key.GetProperty("kid").GetString().Should().Be(jwt.Kid).And.NotBeNullOrEmpty();
        key.GetProperty("kty").GetString().Should().Be("EC");
        key.TryGetProperty("d", out _).Should().BeFalse("the private half stays with Auth");

        // A host that knows the project by the URL its tokens name, and is told where Auth answers. It has no
        // secret: the key Auth publishes is all it checks the token with.
        var project = new SupabaseAuthOptions { ProjectUrl = SupabaseAuthStack.ProjectUrl, AuthUrl = auth.Url.ToString() };
        await using var host = await BearerApplication.StartAsync(authentication => authentication.AddSupabaseJwtBearer(project), Cancellation);

        (await host.AskAsync(token!)).Should().BeEquivalentTo(new { Status = HttpStatusCode.OK, Body = $"{person.Id}|authenticated {person.Id}" });

        // The same claims under the same key's name, signed with a key of the test's own: not Auth's.
        var forged = AccessTokens.For(
            new SigningCredentials(AccessTokens.NewSigningKey(jwt.Kid), SecurityAlgorithms.EcdsaSha256),
            user: person.Id,
            issuer: jwt.Issuer);
        (await host.AskAsync(forged)).Status.Should().Be(HttpStatusCode.Unauthorized);

        // And without a web framework, as a function or a worker checks it.
        var validation = await new SupabaseTokenValidator(project).ValidateAsync(token!);
        validation.IsValid.Should().BeTrue(validation.Error?.Message);
        validation.Caller.Should().BeEquivalentTo(new { Kind = CallerKind.User, UserId = (Guid?)person.Id });
    }
}
