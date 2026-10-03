using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Security;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Invitations;

/// <summary>
/// Invitations through the GraphQL schema: the three mutations send the commands the routes send, and the open
/// invitations are a query. What <see cref="InvitationScenarios"/> shows of the routes holds here, since the use
/// cases are the same; these show that each field answers what its route answers, and a refusal in the payload.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class InvitationFieldScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Invite =
        $$"""
        mutation($address: String!, $unit: UUID!, $role: UUID!, $until: DateTime, $name: String) {
          personInvite(input: { address: $address, unitId: $unit, roleId: $role, until: $until, displayName: $name }) {
            issuedInvitation { id token expiresAt } {{SampleGraphQLCalls.Errors}}
          }
        }
        """;

    private const string Accept = $$"""mutation($token: String!) { invitationAccept(input: { token: $token }) { seatId {{SampleGraphQLCalls.Errors}} } }""";

    private const string Cancel = $$"""mutation($id: UUID!) { invitationCancel(input: { id: $id }) { invitationId {{SampleGraphQLCalls.Errors}} } }""";

    private const string Open =
        "{ openInvitations { id address displayName until issuedAt expiresAt unit { id name } role { id name } issuedBy { id displayName } } }";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task Tove_invites_juno_and_juno_accepts_and_each_field_answers_what_its_route_answers()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        using var juno = await host.ClientAsync("juno", tenant: null);
        using var junoInMeadow = await host.ClientAsync("juno", Meadow.Slug);
        var surveyor = Meadow.Roles[SampleCatalogue.Surveyor].Value;
        var until = DateTimeOffset.UtcNow.AddDays(90);

        // Inviting answers the id, when the invitation ends and its token: the one answer with the token in it.
        var invited = (await tove.GraphQLDataAsync(Invite, new { address = DemoPeople.Juno.Email, unit = Meadow.Root.Value, role = surveyor, until, name = "Juno" }))
            .GetProperty("personInvite");
        invited.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        var issued = invited.GetProperty("issuedInvitation");
        var token = issued.Text("token")!;
        token.Should().HaveLength(BearerTokens.TokenLength);
        issued.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(5));

        // The open invitations: the one the route lists, with the unit, the role and who issued it as what they
        // are, by name, and no token.
        var open = await tove.GraphQLDataAsync(Open);
        var listed = open.GetProperty("openInvitations").EnumerateArray().Should().ContainSingle().Subject;
        var byRoute = (await tove.GetFromJsonAsync<JsonElement>("/tenancy/invitations", Cancellation)).EnumerateArray().Should().ContainSingle().Subject;
        listed.GetProperty("id").GetGuid().Should().Be(issued.GetProperty("id").GetGuid()).And.Be(byRoute.GetProperty("id").GetGuid());
        listed.Text("address").Should().Be(DemoPeople.Juno.Email).And.Be(byRoute.Text("address"));
        listed.Text("displayName").Should().Be("Juno");
        listed.GetProperty("until").GetDateTimeOffset().Should().Be(byRoute.GetProperty("until").GetDateTimeOffset());
        listed.GetProperty("expiresAt").GetDateTimeOffset().Should().Be(byRoute.GetProperty("expiresAt").GetDateTimeOffset());
        listed.GetProperty("unit").GetProperty("id").GetGuid().Should().Be(Meadow.Root.Value);
        listed.GetProperty("unit").Text("name").Should().Be(Meadow.Name);
        listed.GetProperty("role").GetProperty("id").GetGuid().Should().Be(surveyor);
        listed.GetProperty("role").Text("name").Should().Be("Surveyor");
        listed.GetProperty("issuedBy").GetProperty("id").GetGuid().Should().Be(Meadow.SeatOf(DemoPeople.Tove).Value);
        listed.GetProperty("issuedBy").Text("displayName").Should().Be(DemoPeople.Tove.Name);
        open.GetRawText().Should().NotContain(token, "a token is shown once, to whoever invited");

        // She accepts, signed in and naming no tenant: the token says which. The answer is her new seat's id, and
        // her own seats, the other field that needs no tenant, list it in meadow.
        var accepted = (await juno.GraphQLDataAsync(Accept, new { token })).GetProperty("invitationAccept");
        accepted.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        var seat = accepted.GetProperty("seatId").GetGuid();

        var mine = (await juno.GraphQLDataAsync("{ seatsOfMine { tenant { slug } seat { id displayName } } }")).GetProperty("seatsOfMine");
        var inMeadow = mine.EnumerateArray().Single(of => of.GetProperty("tenant").Text("slug") == Meadow.Slug).GetProperty("seat");
        inMeadow.GetProperty("id").GetGuid().Should().Be(seat);
        inMeadow.Text("displayName").Should().Be("Juno");

        // The seat came with its placement and its role: she is a surveyor at meadow's root, as after the route.
        var me = await junoInMeadow.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(seat);
        me.GetProperty("placements").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("grants").EnumerateArray().Should().ContainSingle().Which.GetProperty("roleId").GetGuid().Should().Be(surveyor);

        // The invitation is no longer open, and to anyone else its token was used: a refusal in the payload.
        (await tove.GraphQLDataAsync(Open)).GetProperty("openInvitations").EnumerateArray().Should().BeEmpty();
        using var leo = await host.ClientAsync("leo", tenant: null);
        var taken = (await leo.GraphQLDataAsync(Accept, new { token })).GetProperty("invitationAccept");
        taken.GetProperty("seatId").ValueKind.Should().Be(JsonValueKind.Null);
        Refusal(taken).Should().Be((TenancyRefusals.InvitationUsed, "conflict"));
    }

    [Fact]
    public async Task Cancelling_answers_the_invitations_id_and_its_token_is_refused_from_then_on()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", tenant: null);

        var issued = (await tove.GraphQLDataAsync(Invite, new { address = DemoPeople.Leo.Email, unit = Meadow.Root.Value, role = Meadow.Roles[SampleCatalogue.Observer].Value }))
            .GetProperty("personInvite").GetProperty("issuedInvitation");
        var (id, token) = (issued.GetProperty("id").GetGuid(), issued.Text("token")!);

        // To somebody of another tenant there is no such invitation.
        var notTheirs = (await ada.GraphQLDataAsync(Cancel, new { id })).GetProperty("invitationCancel");
        notTheirs.GetProperty("invitationId").ValueKind.Should().Be(JsonValueKind.Null);
        Refusal(notTheirs).Should().Be((TenancyRefusals.InvitationNotFound, "not_found"));

        var cancelled = (await tove.GraphQLDataAsync(Cancel, new { id })).GetProperty("invitationCancel");
        cancelled.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        cancelled.GetProperty("invitationId").GetGuid().Should().Be(id);

        (await tove.GraphQLDataAsync(Open)).GetProperty("openInvitations").EnumerateArray().Should().BeEmpty();
        Refusal((await leo.GraphQLDataAsync(Accept, new { token })).GetProperty("invitationAccept")).Should().Be((TenancyRefusals.InvitationCancelled, "conflict"));
        (await leo.GraphQLDataAsync("{ seatsOfMine { seat { id } } }")).GetProperty("seatsOfMine").EnumerateArray().Should().ContainSingle("he got no seat in meadow");
    }

    [Fact]
    public async Task Inviting_without_the_key_is_a_refusal_in_the_payload_and_listing_without_it_an_empty_list()
    {
        // Hana gives roles everywhere and may add no seat: what the route answers with 403, and with an empty list.
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        var refused = (await hana.GraphQLDataAsync(Invite, new { address = "wren@example.test", unit = Harbor.UnitNamed("North Coast").Value, role = Harbor.Roles[SampleCatalogue.Observer].Value }))
            .GetProperty("personInvite");

        refused.GetProperty("issuedInvitation").ValueKind.Should().Be(JsonValueKind.Null);
        Refusal(refused).Should().Be((TenancyRefusals.NotPermitted, "not_permitted"));
        (await hana.GraphQLDataAsync(Open)).GetProperty("openInvitations").EnumerateArray().Should().BeEmpty();
    }

    /// <summary>The one error of a mutation's payload, which is a refusal: its code and its kind.</summary>
    private static (string? Code, string? Kind) Refusal(JsonElement payload)
    {
        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle("the payload was {0}", payload.GetRawText()).Subject;
        error.Text("__typename").Should().Be("RefusalError");
        return (error.Text("code"), error.Text("kind"));
    }
}
