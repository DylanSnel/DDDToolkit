using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// A request becomes a Tenancy caller through the caller's verified identity and the slug it names: an
/// active seat of a tenant that is active too, or nobody and why. The toolkit's system caller is never Tenancy's, and
/// Tenancy's system work never touches the toolkit's caller.
/// </summary>
public class TenantSelectionTests
{
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly TenantId Harbor = new(1);
    private static readonly SeatId AdaAtHarbor = SeatId.CreateSequential();

    private static Task<HostCaller> Resolve(ListedSeats seats, Caller caller, string? slug)
        => new TenantSelection<TenantId, SeatId>(seats).ResolveAsync(caller, slug, default);

    [Fact]
    public async Task An_active_seat_in_an_active_tenant_resolves_to_that_seat()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        var caller = await Resolve(seats, Caller.User(Ada), "harbor");

        caller.Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor));
        caller.Kind.Should().Be(TenancyCallerKind.Seat);
        caller.Refusal.Should().BeNull();
    }

    [Fact]
    public async Task The_selection_without_its_ids_answers_what_it_answers_with_them()
    {
        // What a host's middleware asks, and begins as it is answered, naming no id.
        ITenantSelection selection = new TenantSelection<TenantId, SeatId>(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor));

        (await selection.ResolveAsync(Caller.User(Ada), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor));
        (await selection.ResolveAsync(Caller.User(Ada), " ", TestContext.Current.CancellationToken)).Should().Be(HostCaller.Nobody(TenancyRefusals.TenantRequired));
        (await selection.ResolveAsync(Caller.System, "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated));
    }

    [Fact]
    public async Task No_header_is_tenant_required()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        foreach (var slug in new[] { null, "", "   " })
        {
            var caller = await Resolve(seats, Caller.User(Ada), slug);

            caller.Kind.Should().Be(TenancyCallerKind.Nobody);
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

        var unknown = await Resolve(seats, Caller.User(Ada), "nowhere");
        var notMine = await Resolve(seats, Caller.User(Ada), "quarry");

        unknown.Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated));
        notMine.Should().Be(unknown, "whether a slug exists is not told to someone without a seat in it");
    }

    [Fact]
    public async Task A_suspended_seat_resolves_to_nobody()
    {
        var suspended = await Resolve(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor, seatState: SeatStatus.Suspended), Caller.User(Ada), "harbor");
        var deactivated = await Resolve(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor, seatState: SeatStatus.Deactivated), Caller.User(Ada), "harbor");

        suspended.Should().Be(HostCaller.Nobody(TenancyRefusals.SeatSuspended));
        deactivated.Should().Be(HostCaller.Nobody(TenancyRefusals.SeatSuspended));
    }

    [Fact]
    public async Task An_inactive_tenant_resolves_to_nobody()
    {
        foreach (var status in new[] { TenantStatus.Provisioning, TenantStatus.Suspended, TenantStatus.Closed })
        {
            var caller = await Resolve(new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor, tenantState: status), Caller.User(Ada), "harbor");

            caller.Should().Be(HostCaller.Nobody(TenancyRefusals.TenantInactive), status + " is not active");
        }
    }

    [Fact]
    public async Task Anonymous_and_system_callers_get_nobody()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        foreach (var caller in new[] { Caller.Anonymous, Caller.System, Caller.User(userId: null) })
        {
            (await Resolve(seats, caller, "harbor")).Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated), caller + " has no verified identity");
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
                (await Resolve(seats, Caller.User(Ada, role), slug)).Should().Be(
                    HostCaller.Nobody(TenancyRefusals.NotSeated),
                    $"'{role}' is not a seated role, and it is told what a person without a seat is told, whatever '{slug}' is");
            }
        }

        seats.Lookups.Should().BeEmpty("nothing is looked up for a role that holds no seat, so the answer cannot depend on the seats there are");

        // A token that says nothing about a role is a signed-in user's, and so is a caller made without a token.
        (await Resolve(seats, Callers.FromClaims($$"""{"sub":"{{Ada}}"}"""), "harbor")).Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor));
        (await Resolve(seats, Caller.User(Ada, role: null), "harbor")).Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor));
        (await Resolve(seats, Callers.FromClaims($$"""{"sub":"{{Ada}}","role":"authenticated"}"""), "harbor")).Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor));
    }

    [Fact]
    public async Task A_persons_own_seats_are_listed_only_for_a_seated_token_role()
    {
        var quarry = SeatId.CreateSequential();
        var seats = new ListedSeats()
            .With(Ada, Harbor, "harbor", AdaAtHarbor)
            .With(Ada, new TenantId(2), "quarry", quarry, seatState: SeatStatus.Suspended)
            .With(Guid.NewGuid(), new TenantId(3), "orchard", SeatId.CreateSequential());
        var selection = new TenantSelection<TenantId, SeatId>(seats);
        var cancellation = TestContext.Current.CancellationToken;

        // A signed-in user finds every seat of their own, whatever its status, and nobody else's.
        (await selection.SeatsOfAsync(Caller.User(Ada), cancellation)).Select(seat => seat.Seat).Should().Equal(AdaAtHarbor, quarry);
        seats.Listings.Should().Equal(Ada);

        // A token of hers with another role holds no seat, and learns nothing of the seats her identity has; nor
        // does a caller that is no signed-in user. Nothing is looked up for either.
        seats.Listings.Clear();
        foreach (var caller in new[] { Caller.User(Ada, "analyst"), Caller.User(Ada, "service_role"), Caller.User(Ada, ""), Caller.Anonymous, Caller.System, Caller.SystemIn("tenancy") })
        {
            (await selection.SeatsOfAsync(caller, cancellation)).Should().BeEmpty("{0} is answered what a person without a seat is answered", caller);
        }

        seats.Listings.Should().BeEmpty();

        // The host's own list decides, as it does for a seat in one tenant.
        var membersOnly = new TenantSelectionOptions();
        membersOnly.SeatedTokenRoles.Clear();
        membersOnly.SeatedTokenRoles.Add("member");
        selection = new TenantSelection<TenantId, SeatId>(seats, membersOnly);
        (await selection.SeatsOfAsync(Caller.User(Ada, "member"), cancellation)).Should().HaveCount(2);
        (await selection.SeatsOfAsync(Caller.User(Ada), cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_host_may_seat_another_token_role()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);
        new TenantSelectionOptions().SeatedTokenRoles.Should().Equal([TenantSelectionOptions.AuthenticatedTokenRole], "signed-in users are seated unless the host says otherwise");

        var both = new TenantSelectionOptions();
        both.SeatedTokenRoles.Add("member");
        var selection = new TenantSelection<TenantId, SeatId>(seats, both);

        (await selection.ResolveAsync(Caller.User(Ada, "member"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor));
        (await selection.ResolveAsync(Caller.User(Ada), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor), "the role it had stays seated");
        (await selection.ResolveAsync(Caller.User(Ada, "Member"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated), "a role is matched as the token spells it");
        (await selection.ResolveAsync(Caller.User(Ada, "analyst"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated));

        // A host whose users all carry a role of its own lists that one alone.
        var membersOnly = new TenantSelectionOptions();
        membersOnly.SeatedTokenRoles.Clear();
        membersOnly.SeatedTokenRoles.Add("member");
        selection = new TenantSelection<TenantId, SeatId>(seats, membersOnly);

        (await selection.ResolveAsync(Caller.User(Ada, "member"), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.InSeat(Harbor, AdaAtHarbor));
        (await selection.ResolveAsync(Caller.User(Ada), "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated));
        (await selection.ResolveAsync(Caller.User(Ada, role: null), "harbor", TestContext.Current.CancellationToken)).Should().Be(
            HostCaller.Nobody(TenancyRefusals.NotSeated), "no role counts as authenticated, which this host does not seat");

        // The checks that came before still come first, and the ones after still hold.
        (await selection.ResolveAsync(Caller.Anonymous, "harbor", TestContext.Current.CancellationToken)).Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated));
        (await selection.ResolveAsync(Caller.User(Ada, "member"), " ", TestContext.Current.CancellationToken)).Should().Be(HostCaller.Nobody(TenancyRefusals.TenantRequired));
    }

    [Fact]
    public async Task The_ids_come_from_the_directory_not_the_header()
    {
        var seats = new ListedSeats().With(Ada, Harbor, "harbor", AdaAtHarbor);

        var caller = await Resolve(seats, Caller.User(Ada), "  Harbor ");

        seats.Lookups.Should().Equal([(Ada, "harbor")], "the slug is trimmed and lowercased, and the identity is the caller's own");
        caller.Tenant.Should().Be(Harbor);
        caller.Seat.Should().Be(AdaAtHarbor);
    }

    [Fact]
    public void Nested_scopes_restore_the_previous_caller()
    {
        var first = HostCaller.InSeat(Harbor, AdaAtHarbor);
        var second = HostCaller.SystemIn(new TenantId(2));

        TenancyCallers.Ambient.Should().BeNull();
        TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated), "no caller is nobody");
        TenancyCallers.CurrentTenantOrNull().Should().BeNull();

        using (TenancyCallers.Begin(first))
        {
            TenancyCallers.CurrentTenantOrNull().Should().Be(Harbor);

            using (TenancyCallers.Begin(second))
            {
                TenancyCallers.Current<TenantId, SeatId>().Should().BeSameAs(second);
                TenancyCallers.CurrentTenantOrNull().Should().Be(new TenantId(2));
            }

            TenancyCallers.Current<TenantId, SeatId>().Should().BeSameAs(first);
        }

        TenancyCallers.Ambient.Should().BeNull();

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            TenancyCallers.CurrentTenantOrNull().Should().BeNull("system work outside any tenant reads no tenant's rows");
            FluentActions.Invoking(() => TenancyCallers.Current<TenantId, RoleId>()).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void The_save_check_keeps_each_caller_to_its_own_tenant()
    {
        Refused.With(TenancyRefusals.NotSeated, () => TenancySaveCheck.Check(Harbor));

        using (TenancyCallers.Begin(HostCaller.InSeat(Harbor, AdaAtHarbor)))
        {
            TenancySaveCheck.Check(Harbor);
            Refused.With(TenancyRefusals.OtherTenant, () => TenancySaveCheck.Check(new TenantId(2)));
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor))
        {
            TenancySaveCheck.Check(Harbor);
            Refused.With(TenancyRefusals.OtherTenant, () => TenancySaveCheck.Check(new TenantId(2)));
        }

        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.SeatSuspended)))
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
        Callers.Ambient.Should().BeNull();

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            Callers.Ambient!.IsSystem.Should().BeFalse("Tenancy's system work is not the toolkit's system caller");
            Callers.Ambient.IsSystemIn.Should().BeTrue("it is the scoped system caller, which the policies hold");
            TenancyCallers.Ambient!.Kind.Should().Be(TenancyCallerKind.System);
        }

        var ada = Caller.User(Ada);
        using (Callers.Begin(ada))
        {
            using (TenancyWork.BeginSystemIn(Harbor, (SeatId?)AdaAtHarbor))
            {
                Callers.Ambient!.IsSystem.Should().BeFalse("system work in a tenant never runs past the policies");
                Callers.Ambient.IsSystemIn.Should().BeTrue();
                TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.SystemIn(Harbor, AdaAtHarbor));
            }

            Callers.Ambient.Should().BeSameAs(ada, "the toolkit's caller is the person it was once the work ends");
        }

        Callers.Ambient.Should().BeNull();
        TenancyCallers.Ambient.Should().BeNull();
    }
}
