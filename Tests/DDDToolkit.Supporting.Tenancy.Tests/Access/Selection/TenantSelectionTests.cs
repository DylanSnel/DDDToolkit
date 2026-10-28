using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// A request becomes a Tenancy user through the caller's verified identity and the slug it names: an
/// active seat of a tenant that is active too, or nobody and why. The toolkit's system caller is never Tenancy's, and
/// Tenancy's system work never touches the toolkit's current user.
/// </summary>
public class TenantSelectionTests
{
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly TenantId Harbor = new(1);
    private static readonly SeatId AdaAtHarbor = SeatId.CreateSequential();

    private static Task<HostUser> Resolve(ListedSeats seats, CurrentUserSnapshot caller, string? slug)
        => new TenantSelection<TenantId, SeatId>(seats).ResolveAsync(caller, slug, default);

    [Fact]
    public async Task An_active_seat_in_an_active_tenant_resolves_to_that_seat()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        var caller = await Resolve(seats, CurrentUserSnapshot.User(Ada), "harbor");

        caller.Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor));
        caller.Kind.Should().Be(TenancyUserKind.Seat);
        caller.Refusal.Should().BeNull();
    }

    [Fact]
    public async Task The_selection_without_its_ids_answers_what_it_answers_with_them()
    {
        // What a host's middleware asks, and begins as it is answered, naming no id.
        ITenantSelection selection = new TenantSelection<TenantId, SeatId>(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor));

        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor));
        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada), " ", TestContext.Current.CancellationToken)).Should().Be(HostUser.Nobody(TenancyRefusals.TenantRequired));
        (await selection.ResolveAsync(CurrentUserSnapshot.System, "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated));
    }

    [Fact]
    public async Task No_header_is_tenant_required()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        foreach (var slug in new[] { null, "", "   " })
        {
            var caller = await Resolve(seats, CurrentUserSnapshot.User(Ada), slug);

            caller.Kind.Should().Be(TenancyUserKind.Nobody);
            caller.Refusal.Should().Be(TenancyRefusals.TenantRequired);
        }

        seats.Lookups.Should().BeEmpty("without a tenant there is nothing to look up");
    }

    [Fact]
    public async Task An_unknown_slug_and_someone_elses_tenant_answer_the_same()
    {
        var someoneElse = Guid.NewGuid();
        var seats = new ListedSeats()
            .With(Ada, Harbor, "harbor", AdaAtHarbor)
            .With(someoneElse, new TenantId(2), "quarry", SeatId.CreateSequential());

        var unknown = await Resolve(seats, CurrentUserSnapshot.User(Ada), "nowhere");
        var notMine = await Resolve(seats, CurrentUserSnapshot.User(Ada), "quarry");

        unknown.Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated));
        notMine.Should().Be(unknown, "whether a slug exists is not told to someone without a seat in it");
    }

    [Fact]
    public async Task A_suspended_seat_resolves_to_nobody()
    {
        var suspended = await Resolve(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor, seatState: SeatStatus.Suspended), CurrentUserSnapshot.User(Ada), "harbor");
        var deactivated = await Resolve(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor, seatState: SeatStatus.Deactivated), CurrentUserSnapshot.User(Ada), "harbor");

        suspended.Should().Be(HostUser.Nobody(TenancyRefusals.SeatSuspended));
        deactivated.Should().Be(HostUser.Nobody(TenancyRefusals.SeatSuspended));
    }

    [Fact]
    public async Task An_inactive_tenant_resolves_to_nobody()
    {
        foreach (var status in new[] { TenantStatus.Provisioning, TenantStatus.Suspended, TenantStatus.Closed })
        {
            var caller = await Resolve(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor, tenantState: status), CurrentUserSnapshot.User(Ada), "harbor");

            caller.Should().Be(HostUser.Nobody(TenancyRefusals.TenantInactive), status + " is not active");
        }
    }

    [Fact]
    public async Task Anonymous_and_system_callers_get_nobody()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        foreach (var caller in new[] { CurrentUserSnapshot.Anonymous, CurrentUserSnapshot.System, CurrentUserSnapshot.User(id: null) })
        {
            (await Resolve(seats, caller, "harbor")).Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated), caller + " has no verified identity");
        }

        seats.Lookups.Should().BeEmpty();
    }

    [Fact]
    public async Task A_token_role_that_is_not_seated_is_nobody()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        // Ada has a seat at the harbor. A token of hers with another role, analysts who look across tenants say,
        // holds none: in a tenant she is seated in, in one she is not, and without naming one at all.
        foreach (var role in new[] { "analyst", "service_role", "anon", "Authenticated", "authenticated ", "" })
        {
            foreach (var slug in new[] { "harbor", "nowhere", null })
            {
                (await Resolve(seats, CurrentUserSnapshot.User(Ada, role), slug)).Should().Be(
                    HostUser.Nobody(TenancyRefusals.NotSeated),
                    $"'{role}' is not a seated role, and it is told what a person without a seat is told, whatever '{slug}' is");
            }
        }

        seats.Lookups.Should().BeEmpty("nothing is looked up for a role that holds no seat, so the answer cannot depend on the seats there are");

        // A token that says nothing about a role is a signed-in user's, and so is a caller made without a token.
        (await Resolve(seats, CurrentUserSnapshot.FromClaims($$"""{"sub":"{{Ada}}"}"""), "harbor")).Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor));
        (await Resolve(seats, CurrentUserSnapshot.User(Ada, role: null), "harbor")).Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor));
        (await Resolve(seats, CurrentUserSnapshot.FromClaims($$"""{"sub":"{{Ada}}","role":"authenticated"}"""), "harbor")).Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor));
    }

    [Fact]
    public async Task A_persons_own_seats_are_listed_only_for_a_seated_token_role()
    {
        var quarry = SeatId.CreateSequential();
        var seats = new ListedSeats()
            .With(Ada, Harbor, "harbor", AdaAtHarbor, own: "Ada")
            .With(Ada, new TenantId(2), "quarry", quarry, seatState: SeatStatus.Suspended, own: "Ada Lovelace")
            .With(Guid.NewGuid(), new TenantId(3), "orchard", SeatId.CreateSequential(), own: "Grace");
        var selection = new TenantSelection<TenantId, SeatId>(seats);
        var cancellation = TestContext.Current.CancellationToken;

        // A signed-in user finds every seat of their own, whatever its status, and nobody else's.
        (await selection.SeatsOfAsync<string>(CurrentUserSnapshot.User(Ada), cancellation)).Select(seat => seat.Seat).Should().Equal("Ada", "Ada Lovelace");
        seats.Listings.Should().Equal(Ada);

        // A token of hers with another role holds no seat, and learns nothing of the seats her identity has; nor
        // does a caller that is no signed-in user. Nothing is looked up for either.
        seats.Listings.Clear();
        foreach (var caller in new[] { CurrentUserSnapshot.User(Ada, "analyst"), CurrentUserSnapshot.User(Ada, "service_role"), CurrentUserSnapshot.User(Ada, ""), CurrentUserSnapshot.Anonymous, CurrentUserSnapshot.System, CurrentUserSnapshot.SystemIn("tenancy") })
        {
            (await selection.SeatsOfAsync<string>(caller, cancellation)).Should().BeEmpty("{0} is answered what a person without a seat is answered", caller);
        }

        seats.Listings.Should().BeEmpty();

        // The host's own list decides, as it does for a seat in one tenant.
        var membersOnly = new TenantSelectionOptions();
        membersOnly.SeatedTokenRoles.Clear();
        membersOnly.SeatedTokenRoles.Add("member");
        selection = new TenantSelection<TenantId, SeatId>(seats, membersOnly);
        (await selection.SeatsOfAsync<string>(CurrentUserSnapshot.User(Ada, "member"), cancellation)).Should().HaveCount(2);
        (await selection.SeatsOfAsync<string>(CurrentUserSnapshot.User(Ada), cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_persons_own_seats_are_the_applications_own_each_beside_its_tenant()
    {
        var quarry = SeatId.CreateSequential();
        var seats = new ListedSeats()
            .With(Ada, Harbor, "harbor", AdaAtHarbor, own: "Ada")
            .With(Ada, new TenantId(2), "quarry", quarry, tenantState: TenantStatus.Suspended, own: "Ada Lovelace");
        var selection = new TenantSelection<TenantId, SeatId>(seats);

        // The application's own seat, as it keeps it in each tenant, and of the tenant only what is not on the seat.
        var mine = await selection.SeatsOfAsync<string>(CurrentUserSnapshot.User(Ada), TestContext.Current.CancellationToken);

        mine.Should().Equal(
            new SeatInTenant<string>("Ada", "harbor", "Tenant harbor", TenantStatus.Active),
            new SeatInTenant<string>("Ada Lovelace", "quarry", "Tenant quarry", TenantStatus.Suspended));
        mine.Select(seat => $"{seat.Slug}: {seat.Seat}").Should().Equal(["harbor: Ada", "quarry: Ada Lovelace"], "a picker selects what it shows, as every other answer about a seat does");
    }

    [Fact]
    public async Task A_host_may_seat_another_token_role()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);
        new TenantSelectionOptions().SeatedTokenRoles.Should().Equal([TenantSelectionOptions.AuthenticatedTokenRole], "signed-in users are seated unless the host says otherwise");

        var both = new TenantSelectionOptions();
        both.SeatedTokenRoles.Add("member");
        var selection = new TenantSelection<TenantId, SeatId>(seats, both);

        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada, "member"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor));
        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor), "the role it had stays seated");
        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada, "Member"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated), "a role is matched as the token spells it");
        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada, "analyst"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated));

        // A host whose users all carry a role of its own lists that one alone.
        var membersOnly = new TenantSelectionOptions();
        membersOnly.SeatedTokenRoles.Clear();
        membersOnly.SeatedTokenRoles.Add("member");
        selection = new TenantSelection<TenantId, SeatId>(seats, membersOnly);

        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada, "member"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.InSeat(Harbor, AdaAtHarbor));
        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated));
        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada, role: null), "harbor", TestContext.Current.CancellationToken)).Should().Be(
            HostUser.Nobody(TenancyRefusals.NotSeated), "no role counts as authenticated, which this host does not seat");

        // The checks that came before still come first, and the ones after still hold.
        (await selection.ResolveAsync(CurrentUserSnapshot.Anonymous, "harbor", TestContext.Current.CancellationToken)).Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated));
        (await selection.ResolveAsync(CurrentUserSnapshot.User(Ada, "member"), " ", TestContext.Current.CancellationToken)).Should().Be(HostUser.Nobody(TenancyRefusals.TenantRequired));
    }

    [Fact]
    public async Task The_ids_come_from_the_directory_not_the_header()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        var caller = await Resolve(seats, CurrentUserSnapshot.User(Ada), "  Harbor ");

        seats.Lookups.Should().Equal([(Ada, "harbor")], "the slug is trimmed and lowercased, and the identity is the caller's own");
        caller.Tenant.Should().Be(Harbor);
        caller.Seat.Should().Be(AdaAtHarbor);
    }

    [Fact]
    public void Nested_scopes_restore_the_previous_caller()
    {
        var first = HostUser.InSeat(Harbor, AdaAtHarbor);
        var second = HostUser.SystemIn(new TenantId(2));

        TenancyUsers.Ambient.Should().BeNull();
        TenancyUsers.Current<TenantId, SeatId>().Should().Be(HostUser.Nobody(TenancyRefusals.NotSeated), "no caller is nobody");
        TenancyUsers.CurrentTenantOrNull().Should().BeNull();

        using (TenancyUsers.Begin(first))
        {
            TenancyUsers.CurrentTenantOrNull().Should().Be(Harbor);

            using (TenancyUsers.Begin(second))
            {
                TenancyUsers.Current<TenantId, SeatId>().Should().BeSameAs(second);
                TenancyUsers.CurrentTenantOrNull().Should().Be(new TenantId(2));
            }

            TenancyUsers.Current<TenantId, SeatId>().Should().BeSameAs(first);
        }

        TenancyUsers.Ambient.Should().BeNull();

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            TenancyUsers.CurrentTenantOrNull().Should().BeNull("system work outside any tenant reads no tenant's rows");
            FluentActions.Invoking(() => TenancyUsers.Current<TenantId, RoleId>()).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void The_save_check_keeps_each_caller_to_its_own_tenant()
    {
        Refused.With(TenancyRefusals.NotSeated, () => TenancySaveCheck.Check(Harbor));

        using (TenancyUsers.Begin(HostUser.InSeat(Harbor, AdaAtHarbor)))
        {
            TenancySaveCheck.Check(Harbor);
            Refused.With(TenancyRefusals.OtherTenant, () => TenancySaveCheck.Check(new TenantId(2)));
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor))
        {
            TenancySaveCheck.Check(Harbor);
            Refused.With(TenancyRefusals.OtherTenant, () => TenancySaveCheck.Check(new TenantId(2)));
        }

        using (TenancyUsers.Begin(HostUser.Nobody(TenancyRefusals.SeatSuspended)))
        {
            Refused.With(TenancyRefusals.NotSeated, () => TenancySaveCheck.Check(Harbor));
        }

        // System work outside any tenant acts in none, so every tenant's row is another tenant's to it: provisioning
        // saves as system work in the tenant it makes.
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            Refused.With(TenancyRefusals.OtherTenant, () => TenancySaveCheck.Check(Harbor));
            Refused.With(TenancyRefusals.OtherTenant, () => TenancySaveCheck.Check(new TenantId(2)));
        }
    }

    [Fact]
    public void BeginSystemIn_never_begins_a_BYPASSRLS_caller()
    {
        BegunUser.Should().BeNull();

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            BegunUser!.Kind.Should().NotBe(CurrentUserKind.System, "Tenancy's system work is not the toolkit's system work without a scope");
            BegunUser.Kind.Should().Be(CurrentUserKind.SystemIn, "it is the scoped system caller, which the policies hold");
            TenancyUsers.Ambient!.Kind.Should().Be(TenancyUserKind.System);
        }

        var ada = CurrentUserSnapshot.User(Ada);
        using (TestUser.BeginAs(ada))
        {
            using (TenancyWork.BeginSystemIn(Harbor, (SeatId?)AdaAtHarbor))
            {
                BegunUser!.Kind.Should().NotBe(CurrentUserKind.System, "system work in a tenant never runs past the policies");
                BegunUser.Kind.Should().Be(CurrentUserKind.SystemIn);
                TenancyUsers.Current<TenantId, SeatId>().Should().Be(HostUser.SystemIn(Harbor, AdaAtHarbor));
            }

            BegunUser.Should().BeSameAs(ada, "the toolkit's current user is the person it was once the work ends");
        }

        BegunUser.Should().BeNull();
        TenancyUsers.Ambient.Should().BeNull();
    }
}
