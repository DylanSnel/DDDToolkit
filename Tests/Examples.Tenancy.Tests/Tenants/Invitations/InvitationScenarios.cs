using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Identity;
using DDDToolkit.Security;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Tenants.Invitations;

/// <summary>
/// Inviting a person by their address: whoever may add a seat and give the role invites, the open invitations are
/// listed and one is revoked, and the invited person, signed in, accepts with the token and has a seat, a
/// placement and the role from that one request on. Nothing is given before that, and nothing to anyone else.
/// </summary>
/// <remarks>
/// The people invited here are demonstration people into a tenant they have no seat in, since those are who the
/// dev login signs in. To the host their addresses have accounts already; <c>wren@example.test</c> has none.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks. There an invitation is read and revoked by the seats that manage seats at its unit, its
/// token's digest is read by nobody's role, and accepting runs as the application's own work in the invitation's
/// tenant.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class InvitationScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Invitations = "/tenancy/invitations";

    private const string Accept = "/invitations/accept";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task Tove_invites_juno_by_address_and_juno_has_her_seat_once_she_accepts()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        using var juno = await host.ClientAsync("juno", tenant: null);
        using var junoInMeadow = await host.ClientAsync("juno", Meadow.Slug);
        var surveyor = Meadow.Roles[SampleCatalogue.Surveyor].Value;
        var until = DateTimeOffset.UtcNow.AddDays(90);

        // The answer carries the token, this once, and is kept by no cache.
        using var invited = await tove.PostAsJsonAsync(
            Invitations,
            new { address = DemoPeople.Juno.Email, unitId = Meadow.Root.Value, roleId = surveyor, until },
            Cancellation);
        invited.StatusCode.Should().Be(HttpStatusCode.OK);
        invited.Headers.CacheControl!.NoStore.Should().BeTrue();
        var answer = await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var token = answer.Text("token")!;
        token.Should().HaveLength(BearerTokens.TokenLength);
        answer.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(5));

        // Until she accepts there is the invitation and nothing else: no seat, so meadow refuses her.
        using (var notYet = await junoInMeadow.GetAsync("/me", Cancellation))
        {
            await notYet.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        }

        // She accepts, signed in and naming no tenant: the token says which, and with the name she is shown by in
        // meadow, the module's own field. The answer is her new seat.
        using var accepted = await juno.PostAsJsonAsync(Accept, new { token, displayName = "Juno" }, Cancellation);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var seat = (await accepted.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("seatId").GetGuid();

        // The seat, its placement and its grant are there together: placed at the root, a surveyor until the day asked.
        var me = await junoInMeadow.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(seat);
        me.GetProperty("seat").Text("displayName").Should().Be("Juno");
        var placement = me.GetProperty("placements").EnumerateArray().Should().ContainSingle().Subject;
        placement.GetProperty("unit").GetProperty("id").GetGuid().Should().Be(Meadow.Root.Value);
        placement.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        var grant = placement.GetProperty("grants").EnumerateArray().Should().ContainSingle().Subject;
        grant.GetProperty("roleId").GetGuid().Should().Be(surveyor);
        grant.GetProperty("appliesNow").GetBoolean().Should().BeTrue();
        grant.GetProperty("endsAt").GetDateTimeOffset().Should().BeCloseTo(until, TimeSpan.FromSeconds(1));

        // It is among her seats now, which is where a client reads which tenant it is in; and the invitation is no longer open.
        var mine = await juno.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);
        mine.EnumerateArray().Select(of => of.GetProperty("tenant").Text("slug")).Should().BeEquivalentTo(Harbor.Slug, Meadow.Slug);
        mine.EnumerateArray().Single(of => of.GetProperty("seat").GetProperty("id").GetGuid() == seat).GetProperty("tenant").Text("slug").Should().Be(Meadow.Slug);
        (await OpenAsync(tove)).Should().BeEmpty();

        // Sent again by her, the answer is the seat she has; sent by anyone else, the invitation was used.
        using (var again = await juno.PostAsJsonAsync(Accept, new { token }, Cancellation))
        {
            again.StatusCode.Should().Be(HttpStatusCode.OK);
            (await again.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("seatId").GetGuid().Should().Be(seat);
        }

        using var leo = await host.ClientAsync("leo", tenant: null);
        using (var taken = await leo.PostAsJsonAsync(Accept, new { token }, Cancellation))
        {
            await taken.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.InvitationUsed);
        }
    }

    [Fact]
    public async Task Open_invitations_are_listed_for_who_manages_seats_at_their_unit_and_never_with_their_token()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        var coast = Harbor.UnitNamed("North Coast").Value;
        var observer = Harbor.Roles[SampleCatalogue.Observer].Value;

        var (id, token) = await InvitationForAsync(ada, "wren@example.test", coast, observer);

        // Ada, who invited, and rhea, who manages seats in the north, read it: whom it is for, and what it offers by id.
        foreach (var reader in new[] { ada, rhea })
        {
            var listed = (await OpenAsync(reader)).Should().ContainSingle().Subject;
            listed.GetProperty("id").GetGuid().Should().Be(id);
            listed.Text("address").Should().Be("wren@example.test");
            listed.GetProperty("unitId").GetGuid().Should().Be(coast);
            listed.GetProperty("roleId").GetGuid().Should().Be(observer);
            listed.GetProperty("issuedBy").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Ada).Value);
            listed.GetRawText().Should().NotContain(token, "a token is shown once, to whoever invited");
        }

        // Hana gives roles and manages no seats, and tove is in another tenant: neither reads it.
        (await OpenAsync(hana)).Should().BeEmpty();
        (await OpenAsync(tove)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_revoked_invitation_is_no_longer_listed_and_is_refused_when_it_is_accepted()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", tenant: null);

        var (id, token) = await InvitationForAsync(tove, DemoPeople.Leo.Email, Meadow.Root.Value, Meadow.Roles[SampleCatalogue.Observer].Value);

        // To somebody of another tenant there is no such invitation.
        using (var notTheirs = await ada.DeleteAsync($"{Invitations}/{id}", Cancellation))
        {
            await notTheirs.ShouldBeRefusedAsync(HttpStatusCode.NotFound, TenancyRefusals.InvitationNotFound);
        }

        using (var revoked = await tove.DeleteAsync($"{Invitations}/{id}", Cancellation))
        {
            revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await OpenAsync(tove)).Should().BeEmpty();

        using (var tooLate = await leo.PostAsJsonAsync(Accept, new { token }, Cancellation))
        {
            await tooLate.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.InvitationCancelled);
        }

        // He got nothing, and an invitation is revoked once.
        (await leo.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation)).EnumerateArray().Should().ContainSingle();
        using var twice = await tove.DeleteAsync($"{Invitations}/{id}", Cancellation);
        await twice.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.InvitationState);
    }

    [Fact]
    public async Task An_invitation_is_accepted_by_the_address_it_was_sent_to_and_by_nobody_else()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        using var leo = await host.ClientAsync("leo", tenant: null);
        using var juno = await host.ClientAsync("juno", tenant: null);

        // Whatever the case of its letters, the address is juno's.
        var (_, token) = await InvitationForAsync(tove, "Juno@Example.test", Meadow.Root.Value, Meadow.Roles[SampleCatalogue.Observer].Value);

        // Leo holds the token, and his account has another address.
        using (var notHis = await leo.PostAsJsonAsync(Accept, new { token }, Cancellation))
        {
            await notHis.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.AddressMismatch);
        }

        (await leo.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation)).EnumerateArray().Should().ContainSingle("he got no seat in meadow");

        // The refusal used nothing up: the invitation is open still, and hers. An invitation suggests no name: she
        // gives the one she is shown by, which the seat's own rule asks for, and is refused without one, which uses
        // nothing up either.
        (await OpenAsync(tove)).Should().ContainSingle();
        using (var nameless = await juno.PostAsJsonAsync(Accept, new { token, displayName = " " }, Cancellation))
        {
            (await nameless.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, Seat.DisplayNameIsValid.ViolationCode)).Argument("Field").Should().Be("displayName");
        }

        (await OpenAsync(tove)).Should().ContainSingle();

        using var hers = await juno.PostAsJsonAsync(Accept, new { token, displayName = "Juno" }, Cancellation);
        hers.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("hana")] // gives roles everywhere, and may add no seat
    [InlineData("rhea")] // manages seats in the north, not for the whole tenant
    public async Task Inviting_takes_what_adding_a_seat_and_giving_the_role_take(string person)
    {
        using var client = await sample.ClientAsync(person, Harbor.Slug);

        using var response = await client.PostAsJsonAsync(
            Invitations,
            new { address = "wren@example.test", unitId = Harbor.UnitNamed("North Coast").Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
            Cancellation);

        (await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted)).Argument("Key").Should().Be(TenancyKeys.SeatsManage);
    }

    [Fact]
    public async Task What_cannot_be_an_invitation_or_a_token_is_refused_with_its_code()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var tove = await sample.ClientAsync("tove", tenant: null);
        var coast = Harbor.UnitNamed("North Coast").Value;
        var observer = Harbor.Roles[SampleCatalogue.Observer].Value;

        // No address at all, and one that is none.
        using (var missing = await ada.PostAsJsonAsync(Invitations, new { unitId = coast, roleId = observer }, Cancellation))
        {
            await missing.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        }

        using (var noAddress = await ada.PostAsJsonAsync(Invitations, new { address = "wren at example", unitId = coast, roleId = observer }, Cancellation))
        {
            await noAddress.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.AddressInvalid);
        }

        // A role that ends before the invitation itself does offers nothing.
        using (var endsFirst = await ada.PostAsJsonAsync(
            Invitations,
            new { address = "wren@example.test", unitId = coast, roleId = observer, until = DateTimeOffset.UtcNow.AddDays(1) },
            Cancellation))
        {
            await endsFirst.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.InvitationGrantEndsFirst);
        }

        // A text that is no token, and a token nobody was given, are the same to whoever sends them.
        foreach (var token in new[] { "not-a-token", BearerTokens.New().Token })
        {
            using var unknown = await tove.PostAsJsonAsync(Accept, new { token }, Cancellation);
            await unknown.ShouldBeRefusedAsync(HttpStatusCode.NotFound, TenancyRefusals.InvitationNotFound);
        }

        // Accepting needs a token like every route but the dev login's.
        using var anonymous = (await sample.SharedAsync()).Client(token: null, tenant: null);
        using var unsigned = await anonymous.PostAsJsonAsync(Accept, new { token = BearerTokens.New().Token }, Cancellation);
        unsigned.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Somebody_with_a_seat_in_the_tenant_is_not_given_a_second_one()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", tenant: null);

        var (_, token) = await InvitationForAsync(ada, DemoPeople.Tove.Email, Harbor.UnitNamed("North Coast").Value, Harbor.Roles[SampleCatalogue.Observer].Value);

        using var second = await tove.PostAsJsonAsync(Accept, new { token }, Cancellation);
        await second.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.IdentityHasSeat);
    }

    [Fact]
    public async Task Inviting_answers_alike_whether_or_not_the_address_has_an_account()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        var observer = Meadow.Roles[SampleCatalogue.Observer].Value;

        // Juno has an account, and wren gets one made now. Whoever invites cannot tell which from the answers.
        using var known = await tove.PostAsJsonAsync(Invitations, new { address = DemoPeople.Juno.Email, unitId = Meadow.Root.Value, roleId = observer }, Cancellation);
        using var unknown = await tove.PostAsJsonAsync(Invitations, new { address = "wren@example.test", unitId = Meadow.Root.Value, roleId = observer }, Cancellation);

        known.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(known.StatusCode);
        var (first, second) = (await known.Content.ReadFromJsonAsync<JsonElement>(Cancellation), await unknown.Content.ReadFromJsonAsync<JsonElement>(Cancellation));
        second.EnumerateObject().Select(property => (property.Name, property.Value.ValueKind))
            .Should().Equal(first.EnumerateObject().Select(property => (property.Name, property.Value.ValueKind)));
        first.EnumerateObject().Select(property => property.Name).Should().Equal("invitationId", "token", "expiresAt");

        // And the list says the same of both: an address, and nothing about an account.
        var listed = await OpenAsync(tove);
        listed.Should().HaveCount(2);
        listed.Select(invitation => string.Join(",", invitation.EnumerateObject().Select(property => property.Name))).Distinct().Should().ContainSingle();

        // The port was asked for wren: the address has an account now, which it had not.
        var accounts = host.Services.GetRequiredService<IIdentityAccounts>();
        (await accounts.InviteByEmailAsync("wren@example.test", signInRedirect: null, Cancellation)).Should().BeOfType<IdentityAccountOutcome.AddressTaken>("inviting made the account");
    }

    [Fact]
    public async Task When_no_account_can_be_asked_for_the_invitation_is_taken_back()
    {
        await using var host = await sample.StartAsync(services => services.AddSingleton<IIdentityAccounts>(new UnreachableAccounts()));
        using var tove = await host.ClientAsync("tove", Meadow.Slug);

        using var failed = await tove.PostAsJsonAsync(
            Invitations,
            new { address = "wren@example.test", unitId = Meadow.Root.Value, roleId = Meadow.Roles[SampleCatalogue.Observer].Value },
            Cancellation);

        // Nobody was answered its token, so nothing stays open that nobody could accept.
        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await failed.Content.ReadAsStringAsync(Cancellation)).Should().NotContain("wren", "a failure names no address");
        (await OpenAsync(tove)).Should().BeEmpty();
    }

    /// <summary>
    /// The identity provider's mail lets a person in once, and for a while. An address whose mail went unanswered
    /// has an account and no way into it, and the provider answers that the address is taken. The invitation kept
    /// the id of the account the provider made, so inviting the address again has that account mailed again, and
    /// cancelling takes away an account nobody ever used.
    /// </summary>
    [Fact]
    public async Task An_address_whose_mail_went_unanswered_is_mailed_again_and_its_unused_account_goes_with_its_last_invitation()
    {
        var provider = new RecordingAccounts();
        await using var host = await sample.StartAsync(services =>
        {
            services.AddSingleton<IIdentityAccounts>(provider);
            services.AddTenantsInvitationPage(new Uri("https://ui.example.test/invitations/accept"));
        });
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        var (root, observer) = (Meadow.Root.Value, Meadow.Roles[SampleCatalogue.Observer].Value);

        // The first invitation: the provider makes the account, and its mail leads to the page with the token.
        var (first, firstToken) = await InvitationForAsync(tove, "wren@example.test", root, observer);
        var account = provider.Made.Should().ContainSingle().Subject;
        provider.Mails.Should().Equal((account, $"https://ui.example.test/invitations/accept#{firstToken}"));
        (await KeptAccountAsync(host, first)).Should().Be(account, "the invitation keeps the id of the account that was made for it");

        // Invited again, with the address spelt another way: taken, the provider says. The account is this
        // application's own and nobody signed in with it, so it is mailed again, by its id, with the new token.
        var (second, secondToken) = await InvitationForAsync(tove, " Wren@Example.test ", root, observer);
        provider.Made.Should().ContainSingle("no second account is made for one address");
        provider.Mails.Should().HaveCount(2).And.HaveElementAt(1, (account, $"https://ui.example.test/invitations/accept#{secondToken}"));
        (await KeptAccountAsync(host, second)).Should().Be(account);

        // The first invitation is cancelled: it forgets the account, which the second still keeps, so it stays.
        using (var cancelled = await tove.DeleteAsync($"{Invitations}/{first}", Cancellation))
        {
            cancelled.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await KeptAccountAsync(host, first)).Should().BeNull("an invitation that is over keeps nothing about the person it was sent to");
        provider.Deleted.Should().BeEmpty();

        // The last one is cancelled: nobody ever used the account, so it goes, and the address is free again.
        using (var cancelled = await tove.DeleteAsync($"{Invitations}/{second}", Cancellation))
        {
            cancelled.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        provider.Deleted.Should().Equal(account);
        await InvitationForAsync(tove, "wren@example.test", root, observer);
        provider.Made.Should().HaveCount(2, "the address had no account any more, so one was made for the new invitation");
    }

    [Fact]
    public async Task An_account_somebody_signed_in_with_is_neither_mailed_again_nor_deleted()
    {
        var provider = new RecordingAccounts();
        await using var host = await sample.StartAsync(services => services.AddSingleton<IIdentityAccounts>(provider));
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        var (root, observer) = (Meadow.Root.Value, Meadow.Roles[SampleCatalogue.Observer].Value);

        var (first, _) = await InvitationForAsync(tove, "wren@example.test", root, observer);
        var account = provider.Made.Should().ContainSingle().Subject;

        // The person followed the mail: the account is theirs now, and its address is proven.
        provider.SignIn(account);

        // Invited again: the provider mails no account somebody is in, and the invitation keeps none.
        var (second, _) = await InvitationForAsync(tove, "wren@example.test", root, observer);
        provider.Mails.Should().ContainSingle("only the first invitation was mailed");
        (await KeptAccountAsync(host, second)).Should().BeNull();

        // And cancelling both leaves the account where it is: it is a person's.
        foreach (var invitation in new[] { first, second })
        {
            using var cancelled = await tove.DeleteAsync($"{Invitations}/{invitation}", Cancellation);
            cancelled.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        provider.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancellation_that_is_refused_leaves_the_invitation_and_its_account_as_they_were()
    {
        var provider = new RecordingAccounts();
        await using var host = await sample.StartAsync(services => services.AddSingleton<IIdentityAccounts>(provider));
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        var (invitation, _) = await InvitationForAsync(ada, "wren@example.test", Harbor.UnitNamed("North Coast").Value, Harbor.Roles[SampleCatalogue.Observer].Value);
        var account = provider.Made.Should().ContainSingle().Subject;

        // Leo manages no seats: to him there is no such invitation, and nothing about it changes.
        using (var refused = await leo.DeleteAsync($"{Invitations}/{invitation}", Cancellation))
        {
            await refused.ShouldBeRefusedAsync(HttpStatusCode.NotFound, TenancyRefusals.InvitationNotFound);
        }

        provider.Deleted.Should().BeEmpty();
        (await KeptAccountAsync(host, invitation)).Should().Be(account);
        (await OpenAsync(ada)).Should().ContainSingle();
    }

    /// <summary>The account <paramref name="invitation"/> keeps, read as system work in its tenant.</summary>
    private static async Task<Guid?> KeptAccountAsync(SampleFactory host, Guid invitation)
    {
        foreach (var tenant in DemoData.Tenants)
        {
            using (TenancyUseCases.BeginSystemIn(tenant.Id, tenant.Administrator.Id))
            {
                await using var scope = host.Services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<TenancyUseCases.IInvitationStore<Invitation, InvitationId>>();
                if (await store.FindAsync(new InvitationId(invitation), Cancellation) is { } found)
                {
                    return found.InvitedAccount;
                }
            }
        }

        throw new InvalidOperationException($"No tenant has the invitation {invitation}.");
    }

    /// <summary>Invites <paramref name="address"/> for good, and answers the invitation's id and its token.</summary>
    private static async Task<(Guid Id, string Token)> InvitationForAsync(HttpClient inviter, string address, Guid unit, Guid role)
    {
        using var response = await inviter.PostAsJsonAsync(Invitations, new { address, unitId = unit, roleId = role }, Cancellation);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the answer was {0}", await response.Content.ReadAsStringAsync(Cancellation));

        var answer = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        return (answer.GetProperty("invitationId").GetGuid(), answer.Text("token")!);
    }

    /// <summary>The open invitations <paramref name="reader"/> is answered.</summary>
    private static async Task<List<JsonElement>> OpenAsync(HttpClient reader)
        => [.. (await reader.GetFromJsonAsync<JsonElement>(Invitations, Cancellation)).EnumerateArray()];

    /// <summary>
    /// An identity provider that keeps what it was asked: the accounts it made, the mails it sent, each with the
    /// page it leads to, and the accounts it deleted. An account is found by its id, never by its address.
    /// </summary>
    private sealed class RecordingAccounts : IIdentityAccounts
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Guid> _byAddress = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Guid> _signedIn = [];

        /// <summary>The accounts made, in order.</summary>
        public List<Guid> Made { get; } = [];

        /// <summary>The mails sent, in order: the account mailed, and where its link leads.</summary>
        public List<(Guid Account, string? LeadsTo)> Mails { get; } = [];

        /// <summary>The accounts deleted, in order.</summary>
        public List<Guid> Deleted { get; } = [];

        /// <summary>Somebody followed the mail of <paramref name="account"/>: its address is proven from here on.</summary>
        public void SignIn(Guid account)
        {
            lock (_gate)
            {
                _signedIn.Add(account);
            }
        }

        public Task<IdentityAccountOutcome> CreateAsync(Guid identity, string address, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityAccountOutcome> InviteByEmailAsync(string address, Uri? signInRedirect, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_byAddress.ContainsKey(address))
                {
                    return Task.FromResult<IdentityAccountOutcome>(new IdentityAccountOutcome.AddressTaken());
                }

                var made = Guid.NewGuid();
                _byAddress[address] = made;
                Made.Add(made);
                Mails.Add((made, signInRedirect?.AbsoluteUri));
                return Task.FromResult<IdentityAccountOutcome>(new IdentityAccountOutcome.Created(made));
            }
        }

        public Task<IdentityInvitation> InviteAccountAsync(Guid identity, Uri? signInRedirect, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_byAddress.ContainsValue(identity))
                {
                    return Task.FromResult(IdentityInvitation.NoSuchAccount);
                }

                if (_signedIn.Contains(identity))
                {
                    return Task.FromResult(IdentityInvitation.AlreadyProven);
                }

                Mails.Add((identity, signInRedirect?.AbsoluteUri));
                return Task.FromResult(IdentityInvitation.Sent);
            }
        }

        public Task<IdentityAccount?> FindAsync(Guid identity, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_byAddress.ContainsValue(identity) ? new IdentityAccount(identity, _signedIn.Contains(identity)) : null);
            }
        }

        public Task<bool> DeleteAsync(Guid identity, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var address = _byAddress.FirstOrDefault(pair => pair.Value == identity).Key;
                if (address is null)
                {
                    return Task.FromResult(false);
                }

                _byAddress.Remove(address);
                Deleted.Add(identity);
                return Task.FromResult(true);
            }
        }
    }

    /// <summary>An identity provider that cannot be reached, as its adapter reports it.</summary>
    private sealed class UnreachableAccounts : IIdentityAccounts
    {
        public Task<IdentityAccountOutcome> CreateAsync(Guid identity, string address, CancellationToken cancellationToken) => throw Unreachable();

        public Task<IdentityAccountOutcome> InviteByEmailAsync(string address, Uri? signInRedirect, CancellationToken cancellationToken) => throw Unreachable();

        public Task<IdentityInvitation> InviteAccountAsync(Guid identity, Uri? signInRedirect, CancellationToken cancellationToken) => throw Unreachable();

        public Task<IdentityAccount?> FindAsync(Guid identity, CancellationToken cancellationToken) => throw Unreachable();

        public Task<bool> DeleteAsync(Guid identity, CancellationToken cancellationToken) => throw Unreachable();

        private static HttpRequestException Unreachable() => new("The identity provider did not answer.");
    }
}
