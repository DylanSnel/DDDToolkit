using DDDToolkit.Exceptions;
using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// A seat belongs to one verified identity, is placed in a unit at most once and in one unit as its primary,
/// and holds only active roles, each once per placement, for a period whose end comes after its start.
/// </summary>
public class SeatTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    private static readonly OrganizationUnitId Root = OrganizationUnitId.CreateSequential();
    private static readonly OrganizationUnitId North = OrganizationUnitId.CreateSequential();

    private static readonly RoleFacts Administrator = New.ActiveRole(TenancyKeys.RolesManage, TenancyKeys.SeatsManage);
    private static readonly RoleFacts Watcher = New.ActiveRole(HostCatalogue.WidgetRead);

    private static HostSeat PlacedSeat()
    {
        var seat = New.Seat();
        seat.Place(Root, primary: true, Now, placedBy: null);
        seat.DrainEvents();
        return seat;
    }

    [Fact]
    public void A_new_seat_is_active_and_keeps_its_identity()
    {
        var identity = Guid.NewGuid();
        var seat = New.Seat(displayName: "Ada", identity: identity);
        seat.DrainEvents();

        seat.AsScenario().When(candidate => candidate.Rename("Ada Lovelace"))
            .RaisedExactly<SeatRenamed<TenantId, SeatId>>();

        seat.DisplayName.Should().Be("Ada Lovelace");
        seat.Identity.Should().Be(identity, "renaming changes the name, never who the seat belongs to");
        seat.Status.Should().Be(SeatStatus.Active);
        typeof(SeatAggregate<SeatId, TenantId, OrganizationUnitId, RoleId>).GetProperty(nameof(seat.Identity))!.SetMethod!.IsPrivate
            .Should().BeTrue("nothing outside the seat can set the identity, and nothing inside it does after creation");
        seat.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void An_empty_identity_is_refused()
    {
        Refused.With(TenancyRefusals.IdentityRequired, () => New.Seat(identity: Guid.Empty));
        Refused.With(TenancyRefusals.NameInvalid, () => New.Seat(displayName: " ")).Arguments["What"].Should().Be("display-name");
    }

    [Fact]
    public void Placing_twice_in_a_unit_is_refused()
    {
        var scenario = PlacedSeat().AsScenario();

        scenario.WhenThrows<RefusalException>(seat => seat.Place(Root, primary: false, Now, placedBy: null))
            .Code.Should().Be(TenancyRefusals.DuplicatePlacement);
        scenario.Subject.Placements.Should().ContainSingle();
    }

    [Fact]
    public void A_second_primary_is_refused()
    {
        var scenario = PlacedSeat().AsScenario();

        scenario.WhenThrows<RefusalException>(seat => seat.Place(North, primary: true, Now, placedBy: null))
            .Code.Should().Be(TenancyRefusals.SecondPrimary);

        var placed = scenario.When(seat => seat.Place(North, primary: false, Now, placedBy: null))
            .RaisedExactly<SeatPlaced<TenantId, SeatId, OrganizationUnitId>>()
            .SingleEvent<SeatPlaced<TenantId, SeatId, OrganizationUnitId>>();
        placed.UnitId.Should().Be(North);
        placed.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void A_suspended_or_deactivated_seat_is_not_placed_or_granted()
    {
        var suspended = PlacedSeat();
        suspended.Suspend();
        var deactivated = PlacedSeat();
        deactivated.Deactivate();

        foreach (var seat in new[] { suspended, deactivated })
        {
            Refused.With(TenancyRefusals.SeatState, () => seat.Place(North, primary: false, Now, placedBy: null));
            Refused.With(TenancyRefusals.SeatState, () => seat.Grant(Root, RoleId.CreateSequential(), Watcher, GrantPeriod.Open(Now), null, null));
        }

        suspended.Reactivate();
        suspended.Grant(Root, RoleId.CreateSequential(), Watcher, GrantPeriod.Open(Now), null, null);
    }

    [Fact]
    public void A_seat_is_never_placed_by_itself_but_may_be_its_own_granter()
    {
        var seat = PlacedSeat();
        var other = SeatId.CreateSequential();

        Refused.With(TenancyRefusals.SelfAssignment, () => seat.Place(North, primary: false, Now, placedBy: seat.Id));

        // Which roles a seat may give itself is the use case's to decide: the seat only records who gave it.
        seat.Grant(Root, RoleId.CreateSequential(), Watcher, GrantPeriod.Open(Now), grantedBy: seat.Id, null);
        seat.Placements.Single(placement => placement.UnitId == Root).Grants.Single().GrantedBy.Should().Be(seat.Id);

        seat.Place(North, primary: false, Now, placedBy: other).PlacedBy.Should().Be(other);
        seat.Grant(North, RoleId.CreateSequential(), Watcher, GrantPeriod.Open(Now), grantedBy: other, "  covers for Ada ");
        var grant = seat.Placements.Single(placement => placement.UnitId == North).Grants.Single();
        grant.GrantedBy.Should().Be(other);
        grant.Reason.Should().Be("covers for Ada");
    }

    [Fact]
    public void Withdrawing_revokes_its_grants_before_the_withdrawal_event()
    {
        var seat = PlacedSeat();
        seat.Place(North, primary: false, Now, placedBy: null);
        var first = RoleId.CreateSequential();
        var second = RoleId.CreateSequential();
        seat.Grant(North, first, Watcher, GrantPeriod.Open(Now), null, null);
        seat.Grant(North, second, Watcher, GrantPeriod.Open(Now), null, null);
        seat.DrainEvents();

        var raised = seat.AsScenario().When(candidate => candidate.Withdraw(North))
            .RaisedExactly<
                OrganizationRoleRevoked<TenantId, SeatId, OrganizationUnitId, RoleId>,
                OrganizationRoleRevoked<TenantId, SeatId, OrganizationUnitId, RoleId>,
                SeatWithdrawn<TenantId, SeatId, OrganizationUnitId>>();

        raised.EventsOf<OrganizationRoleRevoked<TenantId, SeatId, OrganizationUnitId, RoleId>>().Select(revoked => revoked.RoleId)
            .Should().Equal(first, second);
        raised.SingleEvent<SeatWithdrawn<TenantId, SeatId, OrganizationUnitId>>().UnitId.Should().Be(North);
        seat.Placements.Should().ContainSingle().Which.UnitId.Should().Be(Root);
        Refused.With(TenancyRefusals.PlacementNotFound, () => seat.Withdraw(North));
    }

    [Fact]
    public void Only_an_active_role_is_granted()
    {
        var seat = PlacedSeat();

        Refused.With(TenancyRefusals.RoleNotActive, () => seat.Grant(Root, RoleId.CreateSequential(), Watcher with { IsActive = false }, GrantPeriod.Open(Now), null, null));
        seat.Placements.Single().Grants.Should().BeEmpty();
    }

    [Fact]
    public void A_grant_needs_a_placement_at_its_unit()
    {
        var seat = PlacedSeat();

        Refused.With(TenancyRefusals.PlacementNotFound, () => seat.Grant(North, RoleId.CreateSequential(), Watcher, GrantPeriod.Open(Now), null, null));
    }

    [Fact]
    public void The_same_role_twice_at_a_unit_is_refused()
    {
        var seat = PlacedSeat();
        seat.Place(North, primary: false, Now, placedBy: null);
        var role = RoleId.CreateSequential();
        seat.Grant(Root, role, Watcher, GrantPeriod.Open(Now), null, null);

        Refused.With(TenancyRefusals.DuplicateGrant, () => seat.Grant(Root, role, Watcher, GrantPeriod.Open(Now.AddDays(1)), null, null));
        seat.Grant(North, role, Watcher, GrantPeriod.Open(Now), null, null);

        Refused.With(TenancyRefusals.NameInvalid, () => seat.Grant(Root, RoleId.CreateSequential(), Watcher, GrantPeriod.Open(Now), null, new string('r', 501)))
            .Arguments["Max"].Should().Be(500);
        seat.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_grant_ends_after_it_starts()
    {
        Refused.With(TenancyRefusals.InvalidPeriod, () => GrantPeriod.Between(Now, Now));
        Refused.With(TenancyRefusals.InvalidPeriod, () => GrantPeriod.Between(Now, Now.AddTicks(-1)));

        GrantPeriod.Between(Now, null).Should().Be(GrantPeriod.Open(Now));
        GrantPeriod.Between(Now, Now.AddDays(1)).Ends.Should().Be(Now.AddDays(1));

        var seat = PlacedSeat();
        var role = RoleId.CreateSequential();
        var granted = seat.AsScenario().When(candidate => candidate.Grant(Root, role, Watcher, GrantPeriod.Between(Now, Now.AddDays(1)), null, null))
            .SingleEvent<OrganizationRoleGranted<TenantId, SeatId, OrganizationUnitId, RoleId>>();
        granted.StartsAt.Should().Be(Now);
        granted.EndsAt.Should().Be(Now.AddDays(1));
        granted.GrantedBy.Should().BeNull();
    }

    [Fact]
    public void A_grant_applies_only_inside_its_period()
    {
        var period = GrantPeriod.Between(Now, Now.AddDays(1));

        period.AppliesAt(Now.AddTicks(-1)).Should().BeFalse("it has not started");
        period.AppliesAt(Now).Should().BeTrue("it starts at its first moment");
        period.AppliesAt(Now.AddHours(12)).Should().BeTrue();
        period.AppliesAt(Now.AddDays(1)).Should().BeFalse("it ends at its end, which is the first moment it no longer applies");
        GrantPeriod.Open(Now).AppliesAt(Now.AddYears(10)).Should().BeTrue();

        var seat = PlacedSeat();
        seat.Grant(Root, RoleId.CreateSequential(), Watcher, period, null, null);
        var grant = seat.Placements.Single().Grants.Single();
        grant.AppliesAt(Now.AddHours(12)).Should().BeTrue();
        grant.AppliesAt(Now.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Deactivated_is_final()
    {
        var scenario = PlacedSeat().AsScenario();
        scenario.When(seat => seat.Suspend()).RaisedExactly<SeatSuspended<TenantId, SeatId>>();
        scenario.WhenThrows<RefusalException>(seat => seat.Suspend()).Code.Should().Be(TenancyRefusals.SeatState);
        scenario.When(seat => seat.Deactivate()).RaisedExactly<SeatDeactivated<TenantId, SeatId>>();

        foreach (var transition in new Action<HostSeat>[] { seat => seat.Reactivate(), seat => seat.Suspend(), seat => seat.Deactivate() })
        {
            scenario.WhenThrows<RefusalException>(transition).Code.Should().Be(TenancyRefusals.SeatState);
        }

        scenario.Subject.Status.Should().Be(SeatStatus.Deactivated);
        scenario.Subject.Placements.Should().ContainSingle("a deactivated seat keeps what it had; it grants nothing");
    }

    [Fact]
    public void MakePrimary_moves_the_primary_flag()
    {
        var seat = PlacedSeat();
        seat.Place(North, primary: false, Now, placedBy: null);
        var scenario = seat.AsScenario().IgnorePendingEvents();

        scenario.When(candidate => candidate.MakePrimary(North))
            .RaisedExactly<PrimaryPlacementChanged<TenantId, SeatId, OrganizationUnitId>>()
            .SingleEvent<PrimaryPlacementChanged<TenantId, SeatId, OrganizationUnitId>>().UnitId.Should().Be(North);

        seat.Placements.Single(placement => placement.IsPrimary).UnitId.Should().Be(North);
        seat.Placements.Count(placement => placement.IsPrimary).Should().Be(1);
        scenario.When(candidate => candidate.MakePrimary(North)).RaisedNothing();
        scenario.WhenThrows<RefusalException>(candidate => candidate.MakePrimary(OrganizationUnitId.CreateSequential()))
            .Code.Should().Be(TenancyRefusals.PlacementNotFound);
    }

    [Fact]
    public void Placements_and_grants_are_read_only_views()
    {
        var seat = PlacedSeat();
        seat.Grant(Root, RoleId.CreateSequential(), Watcher, GrantPeriod.Open(Now), null, null);

        var placements = seat.Placements as IList<Placement<SeatId, OrganizationUnitId, RoleId>>;
        var grants = seat.Placements[0].Grants as IList<RoleGrant<SeatId, RoleId>>;

        placements.Should().NotBeNull().And.NotBeOfType<List<Placement<SeatId, OrganizationUnitId, RoleId>>>();
        grants.Should().NotBeNull().And.NotBeOfType<List<RoleGrant<SeatId, RoleId>>>();
        FluentActions.Invoking(() => placements!.Add(placements[0])).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => placements!.Clear()).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => grants!.Add(grants[0])).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => grants!.RemoveAt(0)).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void IsAdministratorAt_counts_only_open_ended_applying_root_grants_of_active_roles_with_the_admin_key()
    {
        var administrator = RoleId.CreateSequential();
        var archived = RoleId.CreateSequential();
        var watcher = RoleId.CreateSequential();
        var unknown = RoleId.CreateSequential();
        var roles = new Dictionary<RoleId, RoleFacts>
        {
            [administrator] = Administrator,
            [archived] = Administrator with { IsActive = false },
            [watcher] = Watcher,
        };
        RoleFacts? Facts(RoleId role) => roles.GetValueOrDefault(role);

        bool Holds(OrganizationUnitId unit, RoleId role, GrantPeriod period, bool suspend = false)
        {
            var seat = New.Seat();
            seat.Place(unit, primary: true, Now, placedBy: null);
            seat.Grant(unit, role, Administrator, period, null, null);
            if (suspend)
            {
                seat.Suspend();
            }

            return seat.IsAdministratorAt(Root, Facts, Now);
        }

        Holds(Root, administrator, GrantPeriod.Open(Now.AddHours(-1))).Should().BeTrue();
        Holds(Root, administrator, GrantPeriod.Open(Now)).Should().BeTrue("a grant applies from its first moment");

        Holds(Root, administrator, GrantPeriod.Between(Now.AddHours(-1), Now.AddYears(1))).Should().BeFalse("a grant with an end date does not count");
        Holds(Root, administrator, GrantPeriod.Open(Now.AddHours(1))).Should().BeFalse("a grant that has not started does not apply");
        Holds(Root, archived, GrantPeriod.Open(Now.AddHours(-1))).Should().BeFalse("an archived role grants nothing");
        Holds(Root, watcher, GrantPeriod.Open(Now.AddHours(-1))).Should().BeFalse("the role does not hold the administrator key");
        Holds(Root, unknown, GrantPeriod.Open(Now.AddHours(-1))).Should().BeFalse("a role nobody can find grants nothing");
        Holds(North, administrator, GrantPeriod.Open(Now.AddHours(-1))).Should().BeFalse("only a grant at the root counts");
        Holds(Root, administrator, GrantPeriod.Open(Now.AddHours(-1)), suspend: true).Should().BeFalse("a suspended seat is no administrator");
    }
}
