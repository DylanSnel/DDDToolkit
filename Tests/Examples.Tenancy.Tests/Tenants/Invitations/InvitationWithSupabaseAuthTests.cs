using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Examples.Tenancy.Tests.Tenants.Invitations;

/// <summary>
/// Inviting a person nobody knows yet, on Supabase's own Postgres and Auth images: the host asks Auth for an
/// account through its admin client, Auth mails the person a link, the mail catcher has the mail, and the person
/// the link lets in accepts the invitation with its token.
/// </summary>
/// <remarks>
/// It needs Docker and Supabase's images, so it carries the samples' traits. The host makes the demonstration
/// people users of the stack's Auth server, so the class takes turns with the others that do.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
[Collection(SampleSupabaseStack.AuthUsers)]
public sealed class InvitationWithSupabaseAuthTests(SampleSupabaseStack stack)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task An_invited_address_is_mailed_by_auth_and_whoever_the_mail_lets_in_accepts_with_the_token()
    {
        await using var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation, withAuth: true);
        _ = sample.Host.Server;
        await sample.WaitUntilTheDatabaseIsPastAsync(DateTimeOffset.UtcNow, Cancellation);
        var auth = sample.Auth!;
        using var tove = await sample.Host.ClientAsync("tove", Meadow.Slug);
        var address = $"wren-{Guid.NewGuid():N}@example.test";
        var surveyor = Meadow.Roles[SampleCatalogue.Surveyor].Value;

        // Tove invites an address Auth has never seen. Nobody signs up on this stack: the account is the host's to ask for.
        var token = await InvitationForAsync(tove, address, surveyor);

        // Auth mailed the address its own link, which carries nothing of the application's: not the invitation's token.
        var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle().Subject;
        mail.Text.Should().Contain("type=invite").And.NotContain(token);

        // The link proves the address and signs the person in, as the account Auth made for it.
        var session = await auth.FollowLinkAsync(mail, Cancellation);
        new JsonWebToken(session).GetClaim("email").Value.Should().Be(address);

        // Signed in, and with the token whoever invited handed over, the person has a seat in meadow as a surveyor.
        using var wren = sample.Host.Client(session, tenant: null);
        using var accepted = await wren.PostAsJsonAsync("/invitations/accept", new { token, displayName = "Wren" }, Cancellation);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, "the answer was {0}", await accepted.Content.ReadAsStringAsync(Cancellation));
        var seat = (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("seatId").GetGuid();

        using var inMeadow = sample.Host.Client(session, Meadow.Slug);
        var me = await inMeadow.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(seat);
        me.GetProperty("seat").Text("displayName").Should().Be("Wren");
        me.GetProperty("placements")[0].GetProperty("grants")[0].GetProperty("roleId").GetGuid().Should().Be(surveyor);

        // An address that has an account, as the demonstration people's have here, is answered the same and is
        // not mailed: its person signs in the way they always do.
        await InvitationForAsync(tove, DemoPeople.Rhea.Email, surveyor);
        (await auth.Mail.SentToAsync(DemoPeople.Rhea.Email, atLeast: 0, Cancellation)).Should().BeEmpty();
    }

    private static async Task<string> InvitationForAsync(HttpClient inviter, string address, Guid role)
    {
        using var invited = await inviter.PostAsJsonAsync("/tenancy/invitations", new { address, unitId = Meadow.Root.Value, roleId = role }, Cancellation);
        invited.StatusCode.Should().Be(HttpStatusCode.OK, "the answer was {0}", await invited.Content.ReadAsStringAsync(Cancellation));

        var answer = await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        answer.EnumerateObject().Select(property => property.Name).Should().Equal("invitationId", "token", "expiresAt");
        return answer.Text("token")!;
    }
}
