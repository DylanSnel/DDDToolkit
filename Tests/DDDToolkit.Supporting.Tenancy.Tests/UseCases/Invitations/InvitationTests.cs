using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Security;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// An invitation offers a seat, placed in a unit with a role there, to whoever holds its token. It is issued for
/// an address by a seat that could add the seat and make the grant itself, by the rules every grant follows; the
/// seat exists only once the invitation is accepted, by a signed-in identity, in one save; and an invitation
/// gives no more than its issuer may still give when it is used. The token is shown once: the store keeps its
/// digest, apart from the invitation, and nothing else of the package ever holds it.
/// </summary>
public class InvitationTests
{
    private const string Address = "wren@example.test";

    private static readonly DateTimeOffset Now = FixedClock.Start;

    /// <summary>The identity of the person who is invited, and has no seat anywhere.</summary>
    private static readonly Guid Wren = Guid.NewGuid();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Who an event says made the change, whatever the event.</summary>
    private static TenancyActor<SeatId>? ByOf(DDDToolkit.Interfaces.IDomainEvent raised)
        => (TenancyActor<SeatId>?)raised.GetType().GetProperty("By")!.GetValue(raised);

    /// <summary>Issues an invitation into North with the watchers' role, as <paramref name="issuer"/>, or as Harbor's administrator.</summary>
    private static Task<HostTenancy.IssuedInvitation<InvitationId>> Issue(
        Harness harness,
        SeatId? issuer = null,
        OrganizationUnitId? unit = null,
        string pack = HostCatalogue.WatcherPack,
        DateTimeOffset? grantUntil = null,
        TimeSpan? lifetime = null,
        string address = Address,
        string? displayName = "Wren")
        => harness.As(issuer ?? harness.Administrator, use => use.Invitations.IssueAsync(
            address, unit ?? harness.Harbor.North, harness.RoleFromPack(pack), grantUntil, displayName, lifetime, Cancellation));

    /// <summary>
    /// A seat that manages seats and grants for the whole tenant and nothing else: it may invite, and holds none of
    /// the keys of the roles it offers, and not every key that manages access.
    /// </summary>
    private static async Task<SeatId> PeopleOffice(Harness harness, DateTimeOffset? until = null)
    {
        var office = await harness.BySystemWork(use => use.Roles.CreateAsync(
            "People office", "Adds people and gives them roles", [TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], Cancellation));
        var seat = await harness.SeatAt("Hana", harness.Harbor.Root);
        await harness.BySystemWork(use => use.Seats.GrantAsync(seat, harness.Harbor.Root, office, until, reason: null, Cancellation));
        return seat;
    }

    // ---------------------------------------------------------------- issuing

    [Fact]
    public async Task Issuing_returns_the_token_once_and_keeps_only_its_digest()
    {
        var harness = Harness.OfHarbor();

        var issued = await Issue(harness, grantUntil: Now.AddDays(90));

        issued.Token.Should().HaveLength(BearerTokens.TokenLength);
        issued.ExpiresAt.Should().Be(Now.AddDays(7), "an invitation stays open for a week unless its issuer says otherwise");
        issued.ToString().Should().NotContain(issued.Token, "an answer that is logged by mistake does not give the token away");

        var invitation = harness.Store.Invitation(issued.Id);
        invitation.State.Should().Be(InvitationState.Open);
        invitation.TenantId.Should().Be(harness.Tenant);
        invitation.Address.Should().Be(Address);
        invitation.UnitId.Should().Be(harness.Harbor.North);
        invitation.RoleId.Should().Be(harness.RoleFromPack(HostCatalogue.WatcherPack));
        invitation.GrantUntil.Should().Be(Now.AddDays(90));
        invitation.DisplayName.Should().Be("Wren");
        invitation.IssuedAt.Should().Be(Now);
        invitation.ExpiresAt.Should().Be(issued.ExpiresAt);
        invitation.IssuedBy.Should().Be(harness.Administrator);
        invitation.IssuedAsSystem.Should().BeFalse();
        harness.Store.SeatsIn(harness.Tenant).Should().ContainSingle("issuing makes no seat: the administrator's is the only one");

        // What is kept of the token is its digest, and the digest is not on the invitation.
        BearerTokens.TryDigest(issued.Token, out var digest).Should().BeTrue();
        harness.Store.KeptDigests.Should().Equal(Convert.ToHexString(digest!));
        typeof(HostInvitation).GetProperties().Should().NotContain(property => property.PropertyType == typeof(byte[]), "an invitation that is read gives no digest");
    }

    [Fact]
    public async Task Issuing_needs_seat_management_for_the_whole_tenant_and_grant_management_at_the_unit()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var seatsOnly = await harness.BySystemWork(use => use.Roles.CreateAsync("Seat desk", "Adds seats", [TenancyKeys.SeatsManage], Cancellation));
        var seatDesk = await harness.SeatAt("Cy", harness.Harbor.Root);
        await harness.BySystemWork(use => use.Seats.GrantAsync(seatDesk, harness.Harbor.Root, seatsOnly, until: null, reason: null, Cancellation));

        // Whoever manages seats in one part of the tree adds no seat to the tenant, by an invitation either.
        var partial = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => Issue(harness, supervisor));
        partial.Arguments["Key"].Should().Be(TenancyKeys.SeatsManage);
        partial.Arguments["Unit"].Should().BeNull("adding a seat is asked for the whole tenant");

        // And an invitation offers a role, so its issuer manages grants where it would be held.
        var noGrants = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => Issue(harness, seatDesk));
        noGrants.Arguments["Key"].Should().Be(TenancyKeys.GrantsManage);
        noGrants.Arguments["Unit"].Should().Be(harness.Harbor.North);

        harness.Store.InvitationsOf(harness.Tenant).Should().BeEmpty();
        harness.Store.KeptDigests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_role_that_manages_no_access_is_offered_without_its_keys_and_may_outlast_the_issuers_own_grant()
    {
        var harness = Harness.OfHarbor();
        var hana = await PeopleOffice(harness, until: Now.AddDays(30));

        // She holds neither widget key, and manages grants for a month: the operators' role goes for good.
        var issued = await Issue(harness, hana, pack: HostCatalogue.OperatorPack, grantUntil: null);

        harness.Store.Invitation(issued.Id).IssuedBy.Should().Be(hana);
    }

    [Fact]
    public async Task A_role_that_manages_access_is_offered_only_with_its_keys_that_do_for_at_least_as_long()
    {
        var harness = Harness.OfHarbor();
        var hana = await PeopleOffice(harness);

        // The supervisors' role manages units too, which she does not hold.
        var beyond = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => Issue(harness, hana, pack: HostCatalogue.SupervisorPack));
        beyond.Arguments["Missing"].Should().Be(TenancyKeys.UnitsManage);
        beyond.Arguments["Role"].Should().Be(harness.RoleFromPack(HostCatalogue.SupervisorPack));

        // An administrator for thirty days holds every key, and offers that role for no longer than that.
        var temporary = await harness.SeatAt("Tess", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(30));

        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => Issue(harness, temporary, pack: HostCatalogue.SupervisorPack, grantUntil: null));
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => Issue(harness, temporary, pack: HostCatalogue.SupervisorPack, grantUntil: Now.AddDays(31)));
        var issued = await Issue(harness, temporary, pack: HostCatalogue.SupervisorPack, grantUntil: Now.AddDays(30));

        harness.Store.Invitation(issued.Id).GrantUntil.Should().Be(Now.AddDays(30));
        harness.Store.InvitationsOf(harness.Tenant).Should().ContainSingle("a refused invitation is not kept");
    }

    [Fact]
    public async Task What_an_invitation_offers_has_to_be_there_and_in_use()
    {
        var harness = Harness.OfHarbor();
        var other = harness.Seed(2, "quarry");
        var retired = await harness.BySystemWork(use => use.Roles.CreateAsync("Tally clerk", "Counts widgets", [HostCatalogue.WidgetRead], Cancellation));
        await harness.BySystemWork(use => use.Roles.ArchiveAsync(retired, Cancellation));
        await harness.BySystemWork(use => use.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, Cancellation));
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);

        Task<HostTenancy.IssuedInvitation<InvitationId>> Offering(OrganizationUnitId unit, RoleId role)
            => harness.As(harness.Administrator, use => use.Invitations.IssueAsync(Address, unit, role, null, null, null, Cancellation));

        await Refused.WithCodeAsync(TenancyRefusals.RoleNotFound, () => Offering(harness.Harbor.North, other.AdministratorRole.Id), "a role of another tenant is not found");
        await Refused.WithCodeAsync(TenancyRefusals.RoleNotActive, () => Offering(harness.Harbor.North, retired));
        (await Refused.WithCodeAsync(TenancyRefusals.UnitNotActive, () => Offering(harness.Harbor.NorthCoast, watcher))).Arguments["Unit"].Should().Be(harness.Harbor.NorthCoast);
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotFound,
            () => harness.BySystemWork(use => use.Invitations.IssueAsync(Address, OrganizationUnitId.CreateSequential(), watcher, null, null, null, Cancellation)));

        // System work in a tenant that is not in use offers nothing either.
        await harness.BySystemWork(use => use.Tenants.SuspendAsync("unpaid", Cancellation));
        await Refused.WithCodeAsync(TenancyRefusals.TenantInactive,
            () => harness.BySystemWork(use => use.Invitations.IssueAsync(Address, harness.Harbor.North, watcher, null, null, null, Cancellation)));

        harness.Store.InvitationsOf(harness.Tenant).Should().BeEmpty();
    }

    [Fact]
    public async Task A_lifetime_outside_its_bounds_and_a_grant_that_ends_first_are_refused_and_name_their_input()
    {
        var harness = Harness.OfHarbor();

        foreach (var lifetime in new[] { TimeSpan.FromMinutes(9), TimeSpan.FromDays(31), TimeSpan.Zero, TimeSpan.FromDays(-1) })
        {
            var outside = await Refused.WithCodeAsync(TenancyRefusals.InvitationLifetime, () => Issue(harness, lifetime: lifetime));
            outside.Arguments["Min"].Should().Be(10L);
            outside.Arguments["Max"].Should().Be(30L * 24 * 60);
            outside.Arguments[RefusalException.FieldArgument].Should().Be("lifetime");
            outside.Message.Should().Be("An invitation stays open for 10 to 43200 minutes.");
        }

        (await Issue(harness, lifetime: TimeSpan.FromMinutes(10))).ExpiresAt.Should().Be(Now.AddMinutes(10));
        (await Issue(harness, lifetime: TimeSpan.FromDays(30))).ExpiresAt.Should().Be(Now.AddDays(30));

        // A role that would be over before the invitation is, or the moment it is, could be accepted for nothing.
        foreach (var grantUntil in new[] { Now.AddDays(7), Now.AddDays(1), Now.AddDays(-1) })
        {
            var first = await Refused.WithCodeAsync(TenancyRefusals.InvitationGrantEndsFirst, () => Issue(harness, grantUntil: grantUntil));
            first.Arguments[RefusalException.FieldArgument].Should().Be("grantUntil");
        }

        (await Issue(harness, grantUntil: Now.AddDays(7).AddSeconds(1))).ExpiresAt.Should().Be(Now.AddDays(7));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wren")]
    [InlineData("@example.test")]
    [InlineData("wren@")]
    [InlineData("wren@@example.test")]
    [InlineData("wren@example.test, ada@example.test")]
    [InlineData("Wren <wren@example.test>")]
    [InlineData("wren @example.test")]
    public async Task An_invitation_is_for_one_address(string address)
    {
        var harness = Harness.OfHarbor();

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.AddressInvalid, () => Issue(harness, address: address));

        refusal.Arguments[RefusalException.FieldArgument].Should().Be("address");
        refusal.Arguments["Max"].Should().Be(254);
        refusal.Arguments.Values.Should().NotContain(address, "what was sent is not repeated in the answer");
        harness.Store.InvitationsOf(harness.Tenant).Should().BeEmpty();
    }

    [Fact]
    public async Task An_address_is_kept_as_it_was_given_without_the_space_around_it()
    {
        var harness = Harness.OfHarbor();

        var issued = await Issue(harness, address: "  Wren.Marsh@Example.Test ", displayName: "  ");
        await Refused.WithCodeAsync(TenancyRefusals.AddressInvalid, () => Issue(harness, address: new string('w', 243) + "@example.test"));
        await Refused.WithCodeAsync(TenancyRefusals.NameInvalid, () => Issue(harness, displayName: new string('n', 201)));

        var invitation = harness.Store.Invitation(issued.Id);
        invitation.Address.Should().Be("Wren.Marsh@Example.Test");
        invitation.DisplayName.Should().BeNull("a blank name suggests nothing");
        invitation.IsFor("wren.marsh@example.test").Should().BeTrue();
        invitation.IsFor(" WREN.MARSH@EXAMPLE.TEST ").Should().BeTrue();
        invitation.IsFor("wren@example.test").Should().BeFalse();
        invitation.IsFor(null).Should().BeFalse();
    }

    // ---------------------------------------------------------------- accepting

    [Fact]
    public async Task Accepting_makes_the_seat_its_placement_and_its_grant_in_one_save()
    {
        var harness = Harness.OfHarbor();
        var ada = harness.Administrator;
        var issued = await Issue(harness, grantUntil: Now.AddDays(90));
        var saves = harness.Store.SaveCount;
        var events = harness.Store.SavedEvents.Count;
        var revision = harness.Store.RevisionOf(harness.Tenant);
        harness.Clock.Advance(TimeSpan.FromHours(3));

        var accepted = await harness.Accept(Wren, issued.Token, displayName: "Wren Marsh");

        accepted.Tenant.Should().Be(harness.Tenant);
        accepted.Slug.Should().Be("harbor");
        harness.Store.SaveCount.Should().Be(saves + 1, "the seat, its placement, its grant and the invitation are one save");
        harness.Store.RevisionOf(harness.Tenant).Should().Be(revision + 1, "accepting changes who holds what");

        var seat = harness.Store.Seat(accepted.Seat);
        seat.Identity.Should().Be(Wren, "the seat belongs to the verified identity that accepted");
        seat.DisplayName.Should().Be("Wren Marsh");
        seat.Status.Should().Be(SeatStatus.Active);
        var placement = seat.Placements.Should().ContainSingle().Which;
        placement.UnitId.Should().Be(harness.Harbor.North);
        placement.IsPrimary.Should().BeTrue();
        placement.PlacedBy.Should().Be(ada, "the issuer is who placed the seat");
        var grant = placement.Grants.Should().ContainSingle().Which;
        grant.RoleId.Should().Be(harness.RoleFromPack(HostCatalogue.WatcherPack));
        grant.StartsAt.Should().Be(Now.AddHours(3), "the grant starts when the invitation is accepted");
        grant.EndsAt.Should().Be(Now.AddDays(90));
        grant.GrantedBy.Should().Be(ada);
        grant.Reason.Should().Be("invitation");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == accepted.Seat && right.Key == HostCatalogue.WidgetRead && right.UnitId == harness.Harbor.North);

        var invitation = harness.Store.Invitation(issued.Id);
        invitation.State.Should().Be(InvitationState.Accepted);
        invitation.AcceptedAs.Should().Be(accepted.Seat);
        invitation.AcceptedAt.Should().Be(Now.AddHours(3));
        invitation.Address.Should().BeNull("an invitation that is over forgets who it was sent to");
        invitation.DisplayName.Should().BeNull();

        // The work is the application's, in that tenant, for the seat that issued the invitation.
        var raised = harness.Store.SavedEvents.Skip(events).ToList();
        raised.Select(one => one.GetType().GetGenericTypeDefinition()).Should().BeEquivalentTo(
            [typeof(SeatAdded<,>), typeof(SeatPlaced<,,>), typeof(OrganizationRoleGranted<,,,>), typeof(InvitationAccepted<,,>)]);
        raised.Should().OnlyContain(one => ByOf(one) == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, ada));
        raised.OfType<InvitationAccepted<TenantId, InvitationId, SeatId>>().Single().SeatId.Should().Be(accepted.Seat);
    }

    [Fact]
    public async Task Accepting_names_no_tenant_and_works_inside_the_invitations_tenant_alone()
    {
        var harness = Harness.OfHarbor();
        harness.Seed(2, "quarry");
        var issued = await Issue(harness);
        var before = harness.Store.Observed.Count;

        await harness.Accept(Wren, issued.Token);

        // The token is looked up as the person who sent it, with no Tenancy caller; everything after it acts in
        // that one tenant, as the toolkit's scoped system caller, and is saved as system work for the issuer.
        var calls = harness.Store.Observed.Skip(before).ToList();
        calls[0].Name.Should().Be(nameof(InMemoryTenancyStore.FindByDigestAsync));
        calls[0].Tenancy.Should().BeNull();
        calls[0].Core!.Kind.Should().Be(CallerKind.User);
        calls.Skip(1).Should().NotBeEmpty().And.OnlyContain(call =>
            Equals(call.Tenancy!.TenantId, harness.Tenant) && call.Core!.IsSystemIn && call.Core.Scope == TenancyWork.SystemScope);
        var saved = calls.Should().ContainSingle(call => call.Name == nameof(InMemoryTenancyStore.SaveAsync)).Which;
        saved.Tenancy!.Kind.Should().Be(TenancyCallerKind.SystemInTenant);
        saved.Tenancy.SeatId.Should().Be(harness.Administrator, "the work is done for the seat that issued the invitation");
        harness.Store.SeatsIn(new TenantId(2)).Should().ContainSingle("nothing of the other tenant changed");
    }

    [Fact]
    public async Task The_seat_takes_the_name_the_invitation_suggests_unless_the_person_gives_one()
    {
        var harness = Harness.OfHarbor();
        var suggested = await Issue(harness);
        var unnamed = await Issue(harness, displayName: null, address: "finch@example.test");

        var wren = await harness.Accept(Wren, suggested.Token, displayName: " ");
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NameInvalid, () => harness.Accept(Guid.NewGuid(), unnamed.Token, displayName: null));

        harness.Store.Seat(wren.Seat).DisplayName.Should().Be("Wren");
        refusal.Arguments[RefusalException.FieldArgument].Should().Be("displayName");
        harness.Store.Invitation(unnamed.Id).State.Should().Be(InvitationState.Open, "a refused acceptance leaves the invitation as it was");
    }

    [Fact]
    public async Task Accepting_needs_a_signed_in_identity_that_may_hold_a_seat()
    {
        var harness = Harness.OfHarbor();
        harness.Options.OperatorTokenRoles.Add("operator");
        var issued = await Issue(harness);
        var before = harness.Store.Observed.Count;

        // The caller is the toolkit's own, and the method takes none: nothing a host could build is handed in.
        Caller?[] others =
        [
            null,
            Caller.System,
            Caller.SystemIn(TenancyWork.SystemScope),
            Caller.Anonymous,
            Caller.User(null),
            Caller.User(Guid.Empty),
            Caller.User(Wren, "operator"),
            Caller.User(Wren, claim: path => path == "is_anonymous" ? "true" : null),
        ];
        foreach (var other in others)
        {
            using (other is null ? null : Callers.Begin(other))
            {
                harness.Store.BeginUnitOfWork();
                var refusal = await Refused.WithCodeAsync(
                    TenancyRefusals.IdentityRequired,
                    () => harness.Invitations.AcceptAsync(issued.Token, "Wren", verifiedAddress: null, Cancellation),
                    other?.ToString() ?? "with nobody calling");
                refusal.Arguments[RefusalException.FieldArgument].Should().Be("identity");
            }
        }

        harness.Store.Observed.Skip(before).Should().BeEmpty("nobody without an identity learns whether a token exists");
        typeof(HostTenancy.InvitationCommands<HostInvitation, InvitationId>).GetMethod(nameof(HostTenancy.InvitationCommands<HostInvitation, InvitationId>.AcceptAsync))!
            .GetParameters().Should().NotContain(parameter => parameter.ParameterType == typeof(Caller) || parameter.ParameterType == typeof(Guid));

        // A seat of the tenant that is signed in is an identity like any other; a sign-in that says it is not anonymous is one too.
        using (Callers.Begin(Caller.User(Wren, claim: path => path == "is_anonymous" ? "false" : null)))
        {
            harness.Store.BeginUnitOfWork();
            (await harness.Invitations.AcceptAsync(issued.Token, "Wren", verifiedAddress: null, Cancellation)).Tenant.Should().Be(harness.Tenant);
        }
    }

    [Theory]
    [InlineData("nothing like a token")]
    [InlineData("")]
    [InlineData("padded")]
    [InlineData("another")]
    public async Task A_text_that_is_no_token_and_a_token_nobody_was_given_are_not_found(string sent)
    {
        var harness = Harness.OfHarbor();
        var issued = await Issue(harness);
        var token = sent switch
        {
            "padded" => issued.Token + "=",
            "another" => BearerTokens.New().Token,
            _ => sent,
        };
        var before = harness.Store.Observed.Count;

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound, () => harness.Accept(Wren, token));

        string[] asked = sent == "another" ? [nameof(InMemoryTenancyStore.FindByDigestAsync)] : [];
        refusal.Arguments.Should().BeEmpty("the answer repeats nothing of what was sent");
        harness.Store.Observed.Skip(before).Select(call => call.Name).Should().Equal(asked, "a text that is no token has no digest to look up");
        harness.Store.Invitation(issued.Id).State.Should().Be(InvitationState.Open);
    }

    [Fact]
    public async Task An_invitation_runs_out_by_the_clock_and_nothing_has_to_mark_it()
    {
        var harness = Harness.OfHarbor();
        var issued = await Issue(harness, lifetime: TimeSpan.FromHours(1));

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await Refused.WithCodeAsync(TenancyRefusals.InvitationLapsed, () => harness.Accept(Wren, issued.Token));

        var invitation = harness.Store.Invitation(issued.Id);
        invitation.State.Should().Be(InvitationState.Open, "its state is what was last saved; whether it can be accepted is asked of the clock");
        invitation.IsOpenAt(harness.Clock.Now).Should().BeFalse();
        invitation.IsOpenAt(harness.Clock.Now.AddSeconds(-1)).Should().BeTrue();
        (await harness.As(harness.Administrator, use => use.Invitations.ListOpenAsync(Cancellation))).Should().BeEmpty("it is no longer among the open ones");
        harness.Store.SeatsIn(harness.Tenant).Should().ContainSingle();

        // It can still be cancelled, which forgets the address.
        await harness.As(harness.Administrator, use => use.Invitations.CancelAsync(issued.Id, Cancellation));
        harness.Store.Invitation(issued.Id).Address.Should().BeNull();
    }

    [Fact]
    public async Task Accepting_twice_as_the_same_identity_answers_the_same_seat_and_someone_else_is_told_it_was_used()
    {
        var harness = Harness.OfHarbor();
        var issued = await Issue(harness);
        var first = await harness.Accept(Wren, issued.Token);
        var saves = harness.Store.SaveCount;

        var again = await harness.Accept(Wren, issued.Token, displayName: "Somebody else");
        await Refused.WithCodeAsync(TenancyRefusals.InvitationUsed, () => harness.Accept(Guid.NewGuid(), issued.Token));

        again.Should().Be(first, "a request that is sent again is answered what the first was");
        harness.Store.SaveCount.Should().Be(saves, "nothing was saved the second time");
        harness.Store.Seat(first.Seat).DisplayName.Should().Be("Wren");
        harness.Store.SeatsIn(harness.Tenant).Should().HaveCount(2);

        // Long after it would have run out, the person who accepted still gets their answer.
        harness.Clock.Advance(TimeSpan.FromDays(60));
        (await harness.Accept(Wren, issued.Token)).Should().Be(first);
    }

    [Fact]
    public async Task An_identity_with_a_seat_in_the_tenant_cannot_accept_so_nobody_invites_themself()
    {
        var harness = Harness.OfHarbor();
        var issued = await Issue(harness, pack: HostCatalogue.SupervisorPack);

        // The issuer's own identity, with the token it made itself.
        await Refused.WithCodeAsync(TenancyRefusals.IdentityHasSeat, () => harness.Accept(harness.Harbor.Administrator.Identity, issued.Token));

        harness.Store.Invitation(issued.Id).State.Should().Be(InvitationState.Open);
        harness.Store.Seat(harness.Administrator).Placements.Should().ContainSingle();
    }

    [Fact]
    public async Task Accepting_as_another_address_than_the_one_invited_is_refused_when_the_host_says_the_address()
    {
        var harness = Harness.OfHarbor();
        var issued = await Issue(harness);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.AddressMismatch, () => harness.Accept(Wren, issued.Token, verifiedAddress: "finch@example.test"));
        await Refused.WithCodeAsync(TenancyRefusals.AddressMismatch, () => harness.Accept(Wren, issued.Token, verifiedAddress: string.Empty));

        refusal.Arguments.Should().BeEmpty("neither address is repeated");
        refusal.Message.Should().NotContain("example.test");
        harness.Store.Invitation(issued.Id).State.Should().Be(InvitationState.Open);

        // The same address in another case is the same address; and a host that says none holds nobody to one.
        (await harness.Accept(Wren, issued.Token, verifiedAddress: " WREN@Example.Test")).Tenant.Should().Be(harness.Tenant);
        var open = await Issue(harness, address: "lark@example.test");
        (await harness.Accept(Guid.NewGuid(), open.Token, verifiedAddress: null)).Tenant.Should().Be(harness.Tenant);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("suspended")]
    [InlineData("deactivated")]
    [InlineData("the role came to manage access")]
    [InlineData("the issuer's hold ends sooner")]
    public async Task An_invitation_gives_no_more_than_its_issuer_may_still_give_when_it_is_used(string since)
    {
        var harness = Harness.OfHarbor();
        var temporary = since == "the issuer's hold ends sooner";
        var hana = temporary ? await harness.SeatAt("Tess", harness.Harbor.Root, HostCatalogue.AdministratorPack) : await PeopleOffice(harness);
        var issued = await Issue(harness, hana, pack: temporary ? HostCatalogue.SupervisorPack : HostCatalogue.WatcherPack);
        var office = harness.Store.Seat(hana).Placements.Single().Grants.Single().RoleId;

        switch (since)
        {
            case "revoked":
                await harness.BySystemWork(use => use.Seats.RevokeAsync(hana, harness.Harbor.Root, office, Cancellation));
                break;
            case "suspended":
                await harness.BySystemWork(use => use.Seats.SuspendAsync(hana, Cancellation));
                break;
            case "deactivated":
                await harness.BySystemWork(use => use.Seats.DeactivateAsync(hana, Cancellation));
                break;
            case "the role came to manage access":
                // The watchers' role is given a key that manages access, which the issuer does not hold.
                await harness.BySystemWork(use => use.Roles.SetKeysAsync(
                    harness.RoleFromPack(HostCatalogue.WatcherPack), [HostCatalogue.WidgetRead, TenancyKeys.UnitsManage], Cancellation));
                break;
            default:
                // An administrator for good when it issued the supervisors' role for good, and for a month since.
                await harness.BySystemWork(use => use.Seats.RevokeAsync(hana, harness.Harbor.Root, office, Cancellation));
                await harness.Grant(hana, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(30));
                break;
        }

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.InvitationUnbacked, () => harness.Accept(Wren, issued.Token));

        // The person who accepts is no member: the answer names neither the issuer, nor its keys, nor the role.
        refusal.Arguments.Should().BeEmpty();
        refusal.InnerException.Should().BeNull();
        harness.Store.Invitation(issued.Id).State.Should().Be(InvitationState.Open);
        harness.Store.SeatsIn(harness.Tenant).Should().NotContain(seat => seat.Identity == Wren);
    }

    [Fact]
    public async Task An_issuer_that_still_may_is_asked_as_itself_and_not_as_the_system()
    {
        var harness = Harness.OfHarbor();
        var hana = await PeopleOffice(harness);
        var issued = await Issue(harness, hana);
        var before = harness.Store.Observed.Count;

        var accepted = await harness.Accept(Wren, issued.Token);

        // The rights asked about are the issuer's, while the work stays the application's.
        harness.Store.Observed.Skip(before).Should().Contain(call =>
            call.Tenancy != null && call.Tenancy.Kind == TenancyCallerKind.Seat && Equals(call.Tenancy.SeatId, hana) && call.Core!.IsSystemIn);
        harness.Store.Seat(accepted.Seat).Placements.Single().Grants.Single().GrantedBy.Should().Be(hana);
    }

    [Fact]
    public async Task An_invitation_system_work_issued_is_not_held_to_a_seat()
    {
        var harness = Harness.OfHarbor();
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);
        var events = harness.Store.SavedEvents.Count;

        var issued = await harness.BySystemWork(use => use.Invitations.IssueAsync(Address, harness.Harbor.South, supervisors, null, "Wren", null, Cancellation));
        var accepted = await harness.Accept(Wren, issued.Token);

        var invitation = harness.Store.Invitation(issued.Id);
        invitation.IssuedAsSystem.Should().BeTrue();
        invitation.IssuedBy.Should().BeNull();
        var placement = harness.Store.Seat(accepted.Seat).Placements.Single();
        placement.PlacedBy.Should().BeNull("system work with no seat acting placed it");
        placement.Grants.Single().GrantedBy.Should().BeNull();
        harness.Store.SavedEvents.Skip(events).Should().HaveCount(5).And.OnlyContain(one => ByOf(one) == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope));

        // Issued for a seat, it keeps that seat as its issuer, and is still the system's: the seat's rights are not asked.
        var watcher = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.WatcherPack);
        HostTenancy.IssuedInvitation<InvitationId> forBert;
        using (TenancyWork.BeginSystemIn(harness.Tenant, (SeatId?)watcher))
        {
            harness.Store.BeginUnitOfWork();
            forBert = await harness.Invitations.IssueAsync("lark@example.test", harness.Harbor.South, supervisors, null, "Lark", null, Cancellation);
        }

        var lark = await harness.Accept(Guid.NewGuid(), forBert.Token);
        harness.Store.Invitation(forBert.Id).IssuedBy.Should().Be(watcher);
        harness.Store.Seat(lark.Seat).Placements.Single().PlacedBy.Should().Be(watcher);
    }

    [Theory]
    [InlineData("the tenant is suspended", TenancyRefusals.TenantInactive)]
    [InlineData("the unit is archived", TenancyRefusals.UnitNotActive)]
    [InlineData("the role is archived", TenancyRefusals.RoleNotActive)]
    public async Task What_changed_in_the_tenant_since_is_asked_again_at_acceptance(string since, string code)
    {
        var harness = Harness.OfHarbor();
        var issued = await Issue(harness, unit: harness.Harbor.NorthCoast);

        await harness.BySystemWork(use => since switch
        {
            "the tenant is suspended" => use.Tenants.SuspendAsync("unpaid", Cancellation),
            "the unit is archived" => use.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, Cancellation),
            _ => use.Roles.ArchiveAsync(harness.RoleFromPack(HostCatalogue.WatcherPack), Cancellation),
        });

        await Refused.WithCodeAsync(code, () => harness.Accept(Wren, issued.Token));

        harness.Store.Invitation(issued.Id).State.Should().Be(InvitationState.Open);
        harness.Store.SeatsIn(harness.Tenant).Should().ContainSingle();
    }

    [Fact]
    public async Task A_refused_acceptance_leaves_nothing_for_a_later_save_in_its_unit_of_work()
    {
        var harness = Harness.OfHarbor();
        var hana = await PeopleOffice(harness);
        var issued = await Issue(harness, hana);
        await harness.BySystemWork(use => use.Seats.SuspendAsync(hana, Cancellation));

        // A handler that catches the refusal and goes on saves the same unit of work again.
        using (Callers.Begin(Caller.User(Wren)))
        {
            harness.Store.BeginUnitOfWork();
            await Refused.WithCodeAsync(TenancyRefusals.InvitationUnbacked, () => harness.Invitations.AcceptAsync(issued.Token, "Wren", null, Cancellation));
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harness.Tenant))
            {
                await harness.Organization.RenameUnitAsync(harness.Harbor.South, "Southern Region", Cancellation);
            }
        }

        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.South)!.Name.Should().Be("Southern Region", "the later command was saved");
        harness.Store.Invitation(issued.Id).State.Should().Be(InvitationState.Open);
        harness.Store.SeatsIn(harness.Tenant).Should().NotContain(seat => seat.Identity == Wren);
    }

    // ---------------------------------------------------------------- listing and cancelling

    [Fact]
    public async Task Listing_shows_the_open_invitations_into_the_units_where_the_caller_manages_seats()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var watcher = await harness.SeatAt("Cy", harness.Harbor.North, HostCatalogue.WatcherPack);
        var south = await Issue(harness, unit: harness.Harbor.South, address: "south@example.test", lifetime: TimeSpan.FromDays(3));
        var coast = await Issue(harness, unit: harness.Harbor.NorthCoast, address: "coast@example.test", lifetime: TimeSpan.FromDays(2), grantUntil: Now.AddDays(40));
        var north = await Issue(harness, unit: harness.Harbor.North, address: "north@example.test", lifetime: TimeSpan.FromDays(1));
        var accepted = await Issue(harness, address: "taken@example.test");
        var cancelled = await Issue(harness, address: "gone@example.test");
        await harness.Accept(Wren, accepted.Token);
        await harness.As(harness.Administrator, use => use.Invitations.CancelAsync(cancelled.Id, Cancellation));

        var forAda = await harness.As(harness.Administrator, use => use.Invitations.ListOpenAsync(Cancellation));
        var forTheSupervisor = await harness.As(supervisor, use => use.Invitations.ListOpenAsync(Cancellation));
        var forTheWatcher = await harness.As(watcher, use => use.Invitations.ListOpenAsync(Cancellation));
        var forSystemWork = await harness.BySystemWork(use => use.Invitations.ListOpenAsync(Cancellation));

        forAda.Select(invitation => invitation.Id).Should().Equal([north.Id, coast.Id, south.Id], "the open ones, the soonest to end first");
        forTheSupervisor.Select(invitation => invitation.Id).Should().Equal(north.Id, coast.Id);
        forTheWatcher.Should().BeEmpty("a seat that manages no seats is shown no invitation, and is not refused");
        forSystemWork.Should().BeEquivalentTo(forAda);
        forAda[1].Should().Be(new HostTenancy.OpenInvitation<InvitationId>(
            coast.Id, "coast@example.test", harness.Harbor.NorthCoast, harness.RoleFromPack(HostCatalogue.WatcherPack), Now.AddDays(40), "Wren", Now, Now.AddDays(2), harness.Administrator, false));

        // Another tenant's are not among them, whoever asks there.
        var quarry = harness.Seed(2, "quarry");
        (await harness.Run(HostCaller.InSeat(quarry.Tenant.Id, quarry.Administrator.Id), use => use.Invitations.ListOpenAsync(Cancellation))).Should().BeEmpty();
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => harness.Run(HostCaller.Nobody(TenancyRefusals.NotSeated), use => use.Invitations.ListOpenAsync(Cancellation)));
    }

    [Fact]
    public async Task Cancelling_needs_seat_management_at_the_unit_and_an_open_invitation()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var watcher = await harness.SeatAt("Cy", harness.Harbor.North, HostCatalogue.WatcherPack);
        var quarry = harness.Seed(2, "quarry");
        var coast = await Issue(harness, unit: harness.Harbor.NorthCoast);
        var south = await Issue(harness, unit: harness.Harbor.South);
        var events = harness.Store.SavedEvents.Count;

        // An invitation into a unit where the caller manages no seats is not found, as it is not listed either.
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound, () => harness.As(supervisor, use => use.Invitations.CancelAsync(south.Id, Cancellation)));
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound, () => harness.As(watcher, use => use.Invitations.CancelAsync(coast.Id, Cancellation)));
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound,
            () => harness.Run(HostCaller.InSeat(quarry.Tenant.Id, quarry.Administrator.Id), use => use.Invitations.CancelAsync(coast.Id, Cancellation)));
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound, () => harness.As(harness.Administrator, use => use.Invitations.CancelAsync(InvitationId.CreateSequential(), Cancellation)));

        // Whoever manages seats there cancels it, whoever issued it.
        harness.Clock.Advance(TimeSpan.FromHours(2));
        await harness.As(supervisor, use => use.Invitations.CancelAsync(coast.Id, Cancellation));

        var cancelled = harness.Store.Invitation(coast.Id);
        cancelled.State.Should().Be(InvitationState.Cancelled);
        cancelled.ClosedAt.Should().Be(Now.AddHours(2));
        cancelled.Address.Should().BeNull();
        cancelled.DisplayName.Should().BeNull();
        var raised = harness.Store.SavedEvents.Skip(events).Should().ContainSingle()
            .Which.Should().BeOfType<InvitationCancelled<TenantId, InvitationId, SeatId>>().Which;
        raised.TenantId.Should().Be(harness.Tenant);
        raised.InvitationId.Should().Be(coast.Id);
        raised.By.Should().Be(TenancyActor<SeatId>.OfSeat(supervisor));

        // Its token no longer works, and it is cancelled once.
        await Refused.WithCodeAsync(TenancyRefusals.InvitationCancelled, () => harness.Accept(Wren, coast.Token));
        var again = await Refused.WithCodeAsync(TenancyRefusals.InvitationState, () => harness.As(harness.Administrator, use => use.Invitations.CancelAsync(coast.Id, Cancellation)));
        again.Arguments["State"].Should().Be("cancelled");
        again.Arguments["Action"].Should().Be("cancel");

        // One that was accepted is not cancelled: the seat is there, and is what to act on.
        await harness.Accept(Wren, south.Token);
        (await Refused.WithCodeAsync(TenancyRefusals.InvitationState, () => harness.BySystemWork(use => use.Invitations.CancelAsync(south.Id, Cancellation))))
            .Arguments["State"].Should().Be("accepted");
    }

    // ---------------------------------------------------------------- what is kept and passed on

    [Fact]
    public async Task The_token_its_digest_and_the_address_are_in_no_event_and_no_refusal()
    {
        var harness = Harness.OfHarbor();
        var events = harness.Store.SavedEvents.Count;
        var issued = await Issue(harness);
        var other = await Issue(harness, address: "lark@example.test");
        BearerTokens.TryDigest(issued.Token, out var digest).Should().BeTrue();

        var refusals = new List<RefusalException>
        {
            await Refused.WithCodeAsync(TenancyRefusals.AddressMismatch, () => harness.Accept(Wren, issued.Token, verifiedAddress: "finch@example.test")),
            await Refused.WithCodeAsync(TenancyRefusals.IdentityHasSeat, () => harness.Accept(harness.Harbor.Administrator.Identity, issued.Token)),
        };
        await harness.Accept(Wren, issued.Token);
        refusals.Add(await Refused.WithCodeAsync(TenancyRefusals.InvitationUsed, () => harness.Accept(Guid.NewGuid(), issued.Token)));
        await harness.As(harness.Administrator, use => use.Invitations.CancelAsync(other.Id, Cancellation));
        refusals.Add(await Refused.WithCodeAsync(TenancyRefusals.InvitationCancelled, () => harness.Accept(Guid.NewGuid(), other.Token)));

        string[] secrets = [issued.Token, other.Token, Convert.ToHexString(digest!), Convert.ToBase64String(digest!), Address, "lark@example.test", "Wren"];
        var raised = harness.Store.SavedEvents.Skip(events).ToList();
        raised.Select(one => one.GetType().GetGenericTypeDefinition()).Should().Contain(
            [typeof(InvitationIssued<,,,,>), typeof(InvitationAccepted<,,>), typeof(InvitationCancelled<,,>)]);
        foreach (var one in raised)
        {
            var stored = JsonSerializer.Serialize(one, one.GetType());
            secrets.Should().NotContain(secret => stored.Contains(secret, StringComparison.OrdinalIgnoreCase), "an event is stored and may be published: " + stored);
        }

        foreach (var refusal in refusals)
        {
            var told = refusal.Message + " " + string.Join(" ", refusal.Arguments.Select(argument => argument.Key + "=" + argument.Value));
            secrets.Should().NotContain(secret => told.Contains(secret, StringComparison.OrdinalIgnoreCase), "a refusal is shown and logged: " + told);
        }

        // What an event does say: ids, dates and who acted.
        var said = raised.OfType<InvitationIssued<TenantId, InvitationId, OrganizationUnitId, RoleId, SeatId>>().First();
        said.Should().BeEquivalentTo(new
        {
            TenantId = harness.Tenant,
            InvitationId = issued.Id,
            UnitId = harness.Harbor.North,
            RoleId = harness.RoleFromPack(HostCatalogue.WatcherPack),
            ExpiresAt = Now.AddDays(7),
            IssuedBy = (SeatId?)harness.Administrator,
            By = (TenancyActor<SeatId>?)TenancyActor<SeatId>.OfSeat(harness.Administrator),
        });
    }

    [Fact]
    public void An_invitation_made_by_hand_follows_its_own_rules()
    {
        HostInvitation Make(string address = Address, DateTimeOffset? grantUntil = null, DateTimeOffset? expiresAt = null)
            => TenancyInstances.NewInvitation<HostInvitation, InvitationId, TenantId, OrganizationUnitId, RoleId, SeatId>(
                InvitationId.CreateSequential(), new TenantId(1), address, OrganizationUnitId.CreateSequential(), RoleId.CreateSequential(),
                grantUntil, displayName: null, issuedAt: Now, expiresAt ?? Now.AddDays(7));

        var invitation = Make();

        invitation.State.Should().Be(InvitationState.Open);
        invitation.GetInvariantViolations().Should().BeEmpty();
        invitation.IssuedBy.Should().BeNull();
        invitation.IssuedAsSystem.Should().BeFalse();
        Refused.With(TenancyRefusals.AddressInvalid, () => Make(address: "nobody"));
        Refused.With(TenancyRefusals.InvitationGrantEndsFirst, () => Make(grantUntil: Now.AddDays(7)));
        FluentActions.Invoking(() => Make(expiresAt: Now)).Should().Throw<ArgumentException>();

        // The application's own field is saved with the rest, and the package's rules still hold.
        invitation.ChangeNote("See you on Monday.");
        invitation.Cancel(Now.AddHours(1));
        invitation.Note.Should().Be("See you on Monday.");
        invitation.State.Should().Be(InvitationState.Cancelled);
        invitation.GetInvariantViolations().Should().BeEmpty();
        Refused.With(TenancyRefusals.InvitationState, () => invitation.Cancel(Now.AddHours(2)));
    }

    [Fact]
    public async Task The_options_of_invitations_are_checked_where_they_are_registered_and_where_they_are_used()
    {
        var harness = Harness.OfHarbor();
        var broken = new HostTenancy.InvitationCommands<HostInvitation, InvitationId>(
            harness.Store, harness.Store, harness.Catalogue, harness.Options, new TenancyInvitationOptions<InvitationId>(), new AmbientCallerAccessor(), harness.Clock);

        // Made by hand without a way to make an id, a use case still says what is missing.
        (await FluentActions.Awaiting(() => harness.As(harness.Administrator, _ => broken.IssueAsync(
                Address, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.WatcherPack), null, null, null, Cancellation)))
            .Should().ThrowAsync<InvalidOperationException>()).WithMessage("*NewInvitationId is not set*");

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        FluentActions.Invoking(() => Register(services, options => options.NewInvitationId = InvitationId.CreateSequential))
            .Should().Throw<InvalidOperationException>().WithMessage("*call AddTenancy first*");

        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, harness.Options);
        FluentActions.Invoking(() => Register(services, _ => { })).Should().Throw<InvalidOperationException>().WithMessage("*NewInvitationId is not set*");
        FluentActions.Invoking(() => Register(services, options =>
            {
                options.NewInvitationId = InvitationId.CreateSequential;
                options.MinLifetime = TimeSpan.FromDays(8);
            }))
            .Should().Throw<InvalidOperationException>().WithMessage("*lifetimes are out of order*");
        FluentActions.Invoking(() => Register(services, options => options.NewInvitationId = InvitationId.CreateSequential)).Should().NotThrow();

        static void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Action<TenancyInvitationOptions<InvitationId>> configure)
            => services.AddTenancyInvitationsCore<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId, HostInvitation, InvitationId>(configure);
    }
}
