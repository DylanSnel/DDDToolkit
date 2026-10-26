namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Containment is a setting of the catalogue, on unless the application turns it off
/// (<see cref="ApplicationCatalogue.ContainAccessManagingKeys"/>). On, a seat hands on a key that manages access
/// only where it holds it: it gives and takes away such a role with its keys, never gives one to itself, stops a
/// seat that holds one only where it could take it away, changes such a key in a role only as an administrator,
/// and moves a unit only as far as it could give and take away what the move changes. Off, a role or a key that
/// manages access goes as one that manages none. Either way each command asks its key where it acts, a seat gives
/// itself a role for no longer than it holds the grants key there, a move gives the mover nothing, a tenant keeps
/// an administrator, and system work in a tenant, such as a handler that checked a quiz of the application's own,
/// gives what it gives.
/// </summary>
/// <remarks>
/// Ben holds the Grant desk role, <c>tenancy.grants.manage</c> alone, at North. Di is placed at North with
/// nothing. A Supervisor manages units, seats and grants, three keys that manage access.
/// </remarks>
public class ContainmentTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Harbor, with the host's catalogue as it is, or with containment turned off.</summary>
    private static Harness Harbor(bool contained)
        => Harness.OfHarbor(TenancyCatalogue.Build(HostCatalogue.Application with { ContainAccessManagingKeys = contained }, []));

    /// <summary>A role of the tenant's own that holds <paramref name="keys"/> alone, made by system work.</summary>
    private static Task<RoleId> Desk(Harness harness, string name, params string[] keys)
        => harness.BySystemWork(use => use.Roles.CreateAsync(name, "Holds what the test needs", keys, Cancellation));

    /// <summary>Ben, with the Grant desk at North until <paramref name="until"/>, or for good.</summary>
    private static async Task<SeatId> Ben(Harness harness, OrganizationUnitId? at = null, DateTimeOffset? until = null)
    {
        var unit = at ?? harness.Harbor.North;
        var desk = await Desk(harness, "Grant desk", TenancyKeys.GrantsManage);
        var ben = await harness.SeatAt("Ben", unit);
        await harness.BySystemWork(use => use.Seats.GrantAsync(ben, unit, desk, until, reason: null, Cancellation));
        return ben;
    }

    [Fact]
    public async Task With_containment_on_a_grants_manager_hands_on_no_key_that_manages_access_it_does_not_hold()
    {
        var harness = Harbor(contained: true);
        var ben = await Ben(harness);
        var di = await harness.SeatAt("Di", harness.Harbor.North);
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);

        var toDi = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(ben, use => use.Seats.GrantAsync(di, harness.Harbor.North, supervisors, null, null, Cancellation)));
        toDi.Arguments["Missing"].Should().Be(TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage);
        await Refused.WithCodeAsync(TenancyRefusals.SelfAppointment,
            () => harness.As(ben, use => use.Seats.GrantAsync(ben, harness.Harbor.North, supervisors, null, null, Cancellation)));
        harness.Store.Seat(di).Placements.Single().Grants.Should().BeEmpty();

        // A role that manages no access goes to anyone, for as long as he says, as it does either way.
        await harness.As(ben, use => use.Seats.GrantAsync(di, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.OperatorPack), null, null, Cancellation));
        harness.Store.Seat(di).Placements.Single().Grants.Select(grant => grant.RoleId).Should().Equal(harness.RoleFromPack(HostCatalogue.OperatorPack));
    }

    [Fact]
    public async Task With_containment_off_a_role_that_manages_access_is_given_as_one_that_manages_none()
    {
        var harness = Harbor(contained: false);
        var ben = await Ben(harness, until: Now.AddDays(7));
        var di = await harness.SeatAt("Di", harness.Harbor.North);
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);

        // To anyone placed where he manages grants, every role, for as long as he says.
        await harness.As(ben, use => use.Seats.GrantAsync(di, harness.Harbor.North, administrators, null, null, Cancellation));
        harness.Store.Seat(di).Placements.Single().Grants.Single().RoleId.Should().Be(administrators);

        // To himself every role too, for no longer than he holds the grants key there: with it for a week, for a week.
        await harness.As(ben, use => use.Seats.GrantAsync(ben, harness.Harbor.North, supervisors, Now.AddDays(7), null, Cancellation));
        var forGood = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(ben, use => use.Seats.GrantAsync(ben, harness.Harbor.North, administrators, null, null, Cancellation)));
        forGood.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage);

        harness.Store.Seat(ben).Placements.Single().Grants.Single(grant => grant.RoleId == supervisors).EndsAt.Should().Be(Now.AddDays(7));
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == ben && right.Key == TenancyKeys.UnitsManage && right.EndsAt == Now.AddDays(7),
            "with a key that manages access he administers what it reaches");

        // What it does not touch: the key is still asked where the role is given, and Ben gives grants at North alone.
        await harness.Place(di, harness.Harbor.South);
        var elsewhere = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(ben, use => use.Seats.GrantAsync(di, harness.Harbor.South, supervisors, null, null, Cancellation)));
        elsewhere.Arguments["Key"].Should().Be(TenancyKeys.GrantsManage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_seat_gives_itself_a_role_that_manages_no_access_for_no_longer_than_it_holds_the_grants_key_on_or_off(bool contained)
    {
        var harness = Harbor(contained);
        var ben = await Ben(harness, until: Now.AddDays(7));
        var watchers = harness.RoleFromPack(HostCatalogue.WatcherPack);

        var forGood = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(ben, use => use.Seats.GrantAsync(ben, harness.Harbor.North, watchers, null, null, Cancellation)));
        forGood.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage);

        await harness.As(ben, use => use.Seats.GrantAsync(ben, harness.Harbor.North, watchers, Now.AddDays(7), null, Cancellation));
        harness.Store.Seat(ben).Placements.Single().Grants.Single(grant => grant.RoleId == watchers).EndsAt.Should().Be(Now.AddDays(7));
    }

    [Fact]
    public async Task With_containment_off_taking_away_a_role_that_manages_access_takes_the_grants_key_alone()
    {
        var harness = Harbor(contained: false);
        var ben = await Ben(harness);
        var seth = await harness.SeatAt("Seth", harness.Harbor.North, HostCatalogue.SupervisorPack, HostCatalogue.OperatorPack);

        await harness.As(ben, use => use.Seats.RevokeAsync(seth, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.SupervisorPack), Cancellation));

        harness.Store.Seat(seth).Placements.Single().Grants.Select(grant => grant.RoleId).Should().Equal(harness.RoleFromPack(HostCatalogue.OperatorPack));
        harness.Store.SavedRights.Should().NotContain(right => right.SeatId == seth && right.Key == TenancyKeys.UnitsManage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_seats_manager_stops_a_seat_whose_roles_manage_access_it_does_not_hold_only_with_containment_off(bool contained)
    {
        var harness = Harbor(contained);
        var desk = await Desk(harness, "Seat desk", TenancyKeys.SeatsManage);
        var pat = await harness.SeatAt("Pat", harness.Harbor.Root);
        await harness.BySystemWork(use => use.Seats.GrantAsync(pat, harness.Harbor.Root, desk, null, null, Cancellation));
        var seth = await harness.SeatAt("Seth", harness.Harbor.North, HostCatalogue.SupervisorPack);

        if (contained)
        {
            var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => harness.As(pat, use => use.Seats.SuspendAsync(seth, Cancellation)));
            refusal.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.UnitsManage);
            harness.Store.Seat(seth).Status.Should().Be(SeatStatus.Active);
            return;
        }

        await harness.As(pat, use => use.Seats.SuspendAsync(seth, Cancellation));
        harness.Store.Seat(seth).Status.Should().Be(SeatStatus.Suspended);
        await harness.As(pat, use => use.Seats.ReactivateAsync(seth, Cancellation));
        harness.Store.Seat(seth).Status.Should().Be(SeatStatus.Active);

        // The key for the whole tenant is asked either way: Ben manages grants, and no seats.
        var ben = await Ben(harness);
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(ben, use => use.Seats.SuspendAsync(seth, Cancellation)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_role_manager_for_a_week_changes_a_key_that_manages_access_in_a_role_only_with_containment_off(bool contained)
    {
        var harness = Harbor(contained);
        var watchers = harness.RoleFromPack(HostCatalogue.WatcherPack);
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);
        var rhea = await harness.SeatAt("Rhea", harness.Harbor.Root);
        await harness.Grant(rhea, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(7));

        if (contained)
        {
            var adding = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
                () => harness.As(rhea, use => use.Roles.SetKeysAsync(watchers, [HostCatalogue.WidgetRead, TenancyKeys.UnitsManage], Cancellation)));
            adding.Arguments["Missing"].Should().Be(TenancyKeys.AdministratorKey);
            await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => harness.As(rhea, use => use.Roles.ArchiveAsync(supervisors, Cancellation)));
            harness.Store.Role(watchers).Keys.Should().Equal(HostCatalogue.WidgetRead);
            return;
        }

        await harness.As(rhea, use => use.Roles.SetKeysAsync(watchers, [HostCatalogue.WidgetRead, TenancyKeys.UnitsManage], Cancellation));
        await harness.As(rhea, use => use.Roles.ArchiveAsync(supervisors, Cancellation));

        harness.Store.Role(watchers).Keys.Should().Equal(TenancyKeys.UnitsManage, HostCatalogue.WidgetRead);
        harness.Store.Role(supervisors).Status.Should().Be(RoleStatus.Archived);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_move_that_would_give_the_mover_more_is_refused_on_or_off(bool contained)
    {
        var harness = Harbor(contained);
        var keepers = await Desk(harness, "Tree keeper", TenancyKeys.UnitsManage);
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        await harness.Place(bert, harness.Harbor.South);
        await harness.BySystemWork(use => use.Seats.GrantAsync(bert, harness.Harbor.South, keepers, null, null, Cancellation));
        var pier = await harness.As(bert, use => use.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", Cancellation));

        // Under North the pier would be Bert's to supervise, where under South he only shapes the tree. With
        // containment off his keys that manage access count as his own keys like any other, and a move gives him none.
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(bert, use => use.Organization.MoveUnitAsync(pier, harness.Harbor.North, Cancellation)));
        ((string)refusal.Arguments["Missing"]!).Split(", ").Should().Contain([TenancyKeys.GrantsManage, TenancyKeys.SeatsManage, HostCatalogue.WidgetCreate]);
        harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.South);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_move_gives_another_seat_a_key_that_manages_access_the_mover_does_not_hold_only_with_containment_off(bool contained)
    {
        var harness = Harbor(contained);
        var keepers = await Desk(harness, "Tree keeper", TenancyKeys.UnitsManage);
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.BySystemWork(use => use.Seats.GrantAsync(bert, harness.Harbor.North, keepers, null, null, Cancellation));
        await harness.Place(bert, harness.Harbor.South);
        await harness.BySystemWork(use => use.Seats.GrantAsync(bert, harness.Harbor.South, keepers, null, null, Cancellation));
        await harness.SeatAt("Seth", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var pier = await harness.As(bert, use => use.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", Cancellation));

        // Under North the pier would be Seth's to supervise, which Bert, who only shapes the tree, could not give him.
        var moving = () => harness.As(bert, use => use.Organization.MoveUnitAsync(pier, harness.Harbor.North, Cancellation));
        if (contained)
        {
            var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, moving);
            refusal.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.SeatsManage);
            harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.South);
            return;
        }

        await moving();
        harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.North);

        // The units key at both parents is asked either way.
        var ben = await Ben(harness);
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(ben, use => use.Organization.MoveUnitAsync(pier, harness.Harbor.South, Cancellation)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_invitation_offers_a_role_that_manages_access_its_issuer_does_not_hold_only_with_containment_off(bool contained)
    {
        var harness = Harbor(contained);
        var office = await Desk(harness, "People office", TenancyKeys.SeatsManage, TenancyKeys.GrantsManage);
        var hana = await harness.SeatAt("Hana", harness.Harbor.Root);
        await harness.BySystemWork(use => use.Seats.GrantAsync(hana, harness.Harbor.Root, office, null, null, Cancellation));
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);

        var issuing = () => harness.As(hana, use => use.Invitations.IssueAsync("wren@example.test", harness.Harbor.South, supervisors, null, null, Cancellation));
        if (contained)
        {
            var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, issuing);
            refusal.Arguments["Missing"].Should().Be(TenancyKeys.UnitsManage);
            return;
        }

        var issued = await issuing();
        var accepted = await harness.Accept(Guid.NewGuid(), issued.Token);

        var grant = harness.Store.Seat(accepted.Seat).Placements.Single().Grants.Single();
        grant.RoleId.Should().Be(supervisors, "accepting asks the issuer again, by the same setting");
    }

    [Fact]
    public async Task With_containment_off_a_tenant_still_keeps_an_administrator()
    {
        var harness = Harbor(contained: false);
        var ben = await Ben(harness, at: harness.Harbor.Root);
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);

        // Ben may take Ada's role away, as far as containment goes, and still not the last administrator's.
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin,
            () => harness.As(ben, use => use.Seats.RevokeAsync(harness.Administrator, harness.Harbor.Root, administrators, Cancellation)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin,
            () => harness.As(harness.Administrator, use => use.Roles.SetKeysAsync(administrators, [HostCatalogue.WidgetRead], Cancellation)));

        // Once he gave himself the role, there are two of them, and Ada's goes.
        await harness.As(ben, use => use.Seats.GrantAsync(ben, harness.Harbor.Root, administrators, null, null, Cancellation));
        await harness.As(ben, use => use.Seats.RevokeAsync(harness.Administrator, harness.Harbor.Root, administrators, Cancellation));

        harness.Store.Seat(harness.Administrator).Placements.Single().Grants.Should().BeEmpty();
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(ben, use => use.Seats.SuspendAsync(ben, Cancellation)));
    }

    [Fact]
    public async Task A_handler_that_checked_a_quiz_itself_gives_the_calling_seat_a_role_that_manages_access_as_system_work_with_containment_on()
    {
        var harness = Harbor(contained: true);
        var quinn = await harness.SeatAt("Quinn", harness.Harbor.North, HostCatalogue.WatcherPack);
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);

        // Quinn holds nothing that manages access, and gives herself nothing that does.
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(quinn, use => use.Seats.GrantAsync(quinn, harness.Harbor.North, supervisors, null, null, Cancellation)));

        await harness.As(quinn, use => PassQuizAsync(use, score: 9));

        var grant = harness.Store.Seat(quinn).Placements.Single().Grants.Single(grant => grant.RoleId == supervisors);
        grant.GrantedBy.Should().BeNull("system work for the very seat it gives to records no seat as the giver");
        grant.Reason.Should().Be("passed the supervisors' quiz");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == quinn && right.Key == TenancyKeys.GrantsManage);

        // The application's own check is the application's: a score too low gives nothing, and system work, which is
        // no seat that passed anything, is turned away.
        await FluentActions.Awaiting(() => harness.As(quinn, use => PassQuizAsync(use, score: 3))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => harness.BySystemWork(use => PassQuizAsync(use, score: 9))).Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// The application's own handler, run as the seat that took the quiz. It checks the quiz itself, and then gives
    /// the role as Tenancy's system work in the tenant, for that seat. System work holds every key there, so
    /// containment asks it nothing, and it gives whatever it is told: the tenant and the seat come from the caller,
    /// and the role and the unit from the quiz's own record, here the supervisors' quiz at North, never from the
    /// request.
    /// </summary>
    private static async Task PassQuizAsync(Harness harness, int score)
    {
        var caller = TenancyCallers.Current<TenantId, SeatId>();
        if (caller is not { Kind: TenancyCallerKind.Seat, Tenant: { } tenant, Seat: { } seat })
        {
            throw new InvalidOperationException("A quiz is passed by a seat.");
        }

        if (score < 8)
        {
            throw new InvalidOperationException("Eight answers out of ten pass the quiz.");
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant, seat))
        {
            await harness.Seats.GrantAsync(seat, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.SupervisorPack), until: null, "passed the supervisors' quiz", Cancellation);
        }
    }
}
