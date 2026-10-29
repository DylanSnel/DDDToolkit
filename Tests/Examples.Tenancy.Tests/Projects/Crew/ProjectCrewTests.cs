using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// The crew's rules as the project itself keeps them, with no host and no database: who is on the crew for which
/// period, which roles each member holds for which period, and what the owner may not lose. Each change goes the way the
/// crew manager makes it, through the project's guard and its hooks (<see cref="ManagedCrew"/>). The scenarios walk the
/// same rules over HTTP; here are the cases a request cannot easily reach, and the events each change raises.
/// </summary>
public sealed class ProjectCrewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly SeatId Owner = SeatId.CreateSequential();

    private static readonly SeatId Member = SeatId.CreateSequential();

    private static readonly ProjectRoleId Lead = ProjectRoleId.CreateSequential();

    private static readonly ProjectRoleId Surveyor = ProjectRoleId.CreateSequential();

    private static readonly ProjectRoleId Observer = ProjectRoleId.CreateSequential();

    [Fact]
    public void A_project_opens_with_its_owner_on_the_crew_for_good_holding_no_project_role()
    {
        var project = Opened();

        var owner = project.Crew.Should().ContainSingle().Which;
        owner.MemberId.Should().Be(Owner);
        (owner.StartsAt, owner.EndsAt, owner.AddedBy).Should().Be((Now, null, null));
        owner.Roles.Should().BeEmpty("the owner holds what an owner does by owning the project");
        owner.AppliesAt(Now.AddSeconds(-1)).Should().BeFalse("nothing counts before it starts");
        project.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProjectOpened>("opening is one event: the owner's place is part of it");
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void A_seat_goes_on_the_crew_with_no_role_and_is_given_roles_one_by_one()
    {
        var project = Opened();
        var until = Now.AddDays(30);

        var member = project.Managed().AddMember(Member, null, MemberPeriod.Between(Now, until), Now, Owner);
        member.Roles.Should().BeEmpty("being on the crew and holding a role on it are two things");
        (member.StartsAt, member.EndsAt, member.AddedBy).Should().Be((Now, until, Owner));

        project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, Owner);
        project.Managed().GiveRole(Member, Observer, MemberPeriod.Open(Now), Now, Owner);

        member.Roles.Select(held => (held.RoleId, held.EndsAt, held.GivenBy)).Should().Equal((Surveyor, Now.AddDays(7), Owner), (Observer, null, Owner));
        Raised(project).Skip(1).Should().Equal(
            Bare(new CrewMemberAdded(project.Id, Member)),
            Bare(new CrewRoleGiven(project.Id, Member, Surveyor)),
            Bare(new CrewRoleGiven(project.Id, Member, Observer)));

        // A role counts while its own period applies and the membership's does.
        member.HoldsAt(Surveyor, Now.AddDays(6)).Should().BeTrue();
        member.HoldsAt(Surveyor, Now.AddDays(7)).Should().BeFalse("its own period has ended");
        member.HoldsAt(Observer, Now.AddDays(29)).Should().BeTrue();
        member.HoldsAt(Observer, until).Should().BeFalse("the membership has ended, whatever the role's own dates say");
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void A_role_still_held_is_refused_and_one_that_has_ended_is_replaced()
    {
        var project = Opened();
        project.Managed().AddMember(Member, null, MemberPeriod.Open(Now), Now, Owner);
        project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, Owner);

        // While the first has not ended, at the start of the second: held, whether it applies yet or not.
        foreach (var start in new[] { Now, Now.AddDays(6) })
        {
            var twice = () => project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Open(start), start, Owner);
            var refusal = twice.Should().Throw<RefusalException>().Which;
            refusal.Code.Should().Be(ProjectRefusals.CrewRoleHeld);
            refusal.Arguments.Should().Contain("Seat", Member).And.Contain("Role", Surveyor);
        }

        // Once it has ended, the role is given again, and the new grant takes the old one's place.
        project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Open(Now.AddDays(7)), Now.AddDays(7), null);

        var held = project.Crew.Single(member => member.MemberId == Member).Roles.Should().ContainSingle().Which;
        (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy).Should().Be((Surveyor, Now.AddDays(7), null, null));
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void Taking_a_role_leaves_the_member_on_the_crew_and_refuses_what_is_not_there()
    {
        var project = Opened();
        project.Managed().AddMember(Member, null, MemberPeriod.Open(Now), Now, Owner);
        project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Open(Now), Now, Owner);
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();

        project.Managed().TakeRole(Member, Surveyor);

        project.Crew.Single(member => member.MemberId == Member).Roles.Should().BeEmpty();
        Raised(project).Should().Equal(Bare(new CrewRoleTaken(project.Id, Member, Surveyor)));

        var again = () => project.Managed().TakeRole(Member, Surveyor);
        again.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.CrewRoleNotFound);
        var stranger = () => project.Managed().TakeRole(SeatId.CreateSequential(), Surveyor);
        stranger.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.MemberNotFound);
        var toNobody = () => project.Managed().GiveRole(SeatId.CreateSequential(), Surveyor, MemberPeriod.Open(Now), Now, Owner);
        toNobody.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.MemberNotFound);
    }

    [Fact]
    public void The_owner_stays_on_the_crew_and_is_given_no_project_role()
    {
        var project = Opened();

        // The owner holds what an owner does by owning the project: a project role would give it more, or nothing.
        var given = () => project.Managed().GiveRole(Owner, Surveyor, MemberPeriod.Open(Now), Now, Owner);
        given.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.OwnerProtected);

        var removed = () => project.Managed().RemoveMember(Owner);
        removed.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.OwnerProtected);
        project.Crew.Single().Roles.Should().BeEmpty();
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void Naming_an_owner_who_is_on_the_crew_keeps_their_place_for_good_and_the_old_owner_stays_in_the_role_said()
    {
        var project = Opened();
        project.Managed().AddMember(Member, null, MemberPeriod.Between(Now, Now.AddDays(7)), Now, Owner);
        project.Managed().GiveRole(Member, Lead, MemberPeriod.Between(Now, Now.AddDays(7)), Now, Owner);
        project.Managed().GiveRole(Member, Observer, MemberPeriod.Open(Now), Now, Owner);
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();
        var later = Now.AddDays(1);

        project.Managed().NameOwner(Member, FormerOwner.StaysAs(Surveyor), later);

        // The new owner was on the crew for a week: the membership is for good now, and its roles went, since it holds
        // by owning. The old owner stays on the crew in the role the command said.
        project.OwnerSeatId.Should().Be(Member);
        var newOwner = project.Crew.Single(member => member.MemberId == Member);
        (newOwner.StartsAt, newOwner.EndsAt, newOwner.Roles.Count).Should().Be((Now, null, 0));
        var oldOwner = project.Crew.Single(member => member.MemberId == Owner);
        oldOwner.EndsAt.Should().BeNull();
        oldOwner.Roles.Select(held => (held.RoleId, held.StartsAt)).Should().Equal((Surveyor, later));
        Raised(project).Should().Equal(
            Bare(new CrewRoleTaken(project.Id, Member, Lead)),
            Bare(new CrewRoleTaken(project.Id, Member, Observer)),
            Bare(new CrewRoleGiven(project.Id, Owner, Surveyor)),
            Bare(new OwnerChanged(project.Id, Owner, Member)));
        RulesBrokenBy(project).Should().BeEmpty();

        // Named again, the seat is the owner already; named back, the first owner's role goes, and the second leaves.
        var same = () => project.Managed().NameOwner(Member, FormerOwner.Leaves, later);
        same.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.AlreadyOwner);
        project.Managed().NameOwner(Owner, FormerOwner.Leaves, later);
        project.Crew.Should().ContainSingle().Which.Should().Match<CrewMember>(member => member.MemberId == Owner && member.Roles.Count == 0);
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void Naming_an_owner_whose_membership_had_ended_starts_it_anew_with_no_role()
    {
        var project = Opened();
        project.Managed().AddMember(Member, null, MemberPeriod.Between(Now, Now.AddDays(30)), Now, Owner);
        project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Open(Now), Now, Owner);
        var later = Now.AddDays(90);
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();

        // The membership ran out two months ago, and the surveyor's role, which had no end of its own, with it.
        // Named owner, the seat is on the crew from now on, holding no role.
        project.Managed().NameOwner(Member, FormerOwner.StaysAs(Observer), later);

        var member = project.Crew.Single(candidate => candidate.MemberId == Member);
        (member.StartsAt, member.EndsAt, member.Roles.Count).Should().Be((later, null, 0), "the membership does not read as unbroken since it first began");
        RulesBrokenBy(project).Should().BeEmpty();

        // The role of the membership that ended is taken, and the seat is on the crew again, as one put on it again
        // would be; the owner before stays on as an observer.
        Raised(project).Should().Equal(
            Bare(new CrewRoleTaken(project.Id, Member, Surveyor)),
            Bare(new CrewMemberAdded(project.Id, Member)),
            Bare(new CrewRoleGiven(project.Id, Owner, Observer)),
            Bare(new OwnerChanged(project.Id, Owner, Member)));
    }

    [Fact]
    public void A_seat_whose_membership_ended_goes_on_the_crew_again_and_takes_no_role_until_then()
    {
        var project = Opened();
        project.Managed().AddMember(Member, null, MemberPeriod.Between(Now, Now.AddDays(30)), Now, Owner);
        project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Open(Now), Now, Owner);
        var later = Now.AddDays(90);

        // A role counts only while the membership does, so giving one to a seat whose membership ended would give
        // nothing: it is refused, where it used to answer as if it had worked.
        var given = () => project.Managed().GiveRole(Member, Observer, MemberPeriod.Open(later), later, Owner);
        given.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.MemberNotFound);

        // Put on the crew again, it has a new membership, without the role of the one that ended.
        var again = project.Managed().AddMember(Member, null, MemberPeriod.Open(later), later, Owner);
        project.Crew.Where(candidate => candidate.MemberId == Member).Should().ContainSingle().Which.Should().BeSameAs(again);
        (again.StartsAt, again.EndsAt).Should().Be((later, null));
        again.Roles.Should().BeEmpty();
        RulesBrokenBy(project).Should().BeEmpty();

        // While a membership runs, or is yet to start, the seat is on the crew already.
        var twice = () => project.Managed().AddMember(Member, null, MemberPeriod.Open(later.AddDays(1)), later, Owner);
        twice.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.AlreadyOnCrew);
    }

    [Fact]
    public void Naming_an_owner_who_is_not_on_the_crew_puts_them_on_it()
    {
        var project = Opened();
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();

        project.Managed().NameOwner(Member, FormerOwner.Leaves, Now.AddDays(1));

        project.Crew.Should().ContainSingle().Which.Should().Match<CrewMember>(member => member.MemberId == Member && member.Roles.Count == 0);
        Raised(project).Should().Equal(
            Bare(new CrewMemberAdded(project.Id, Member)),
            Bare(new CrewMemberRemoved(project.Id, Owner)),
            Bare(new OwnerChanged(project.Id, Owner, Member)));
    }

    [Fact]
    public void A_closed_project_changes_no_crew()
    {
        var project = Opened();
        project.Managed().AddMember(Member, null, MemberPeriod.Open(Now), Now, Owner);
        project.Managed().GiveRole(Member, Surveyor, MemberPeriod.Open(Now), Now, Owner);
        project.Close();

        Action[] changes =
        [
            () => project.Managed().AddMember(SeatId.CreateSequential(), null, MemberPeriod.Open(Now), Now, Owner),
            () => project.Managed().GiveRole(Member, Observer, MemberPeriod.Open(Now), Now, Owner),
            () => project.Managed().TakeRole(Member, Surveyor),
            () => project.Managed().RemoveMember(Member),
            () => project.Managed().NameOwner(Member, FormerOwner.Leaves, Now),
        ];

        foreach (var change in changes)
        {
            change.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.Closed);
        }
    }

    /// <summary>A project opened now, owned by <see cref="Owner"/>, who is on its crew holding no project role.</summary>
    private static Project Opened()
        => new(ProjectId.CreateSequential(), TenantId.CreateSequential(), "P-001", "Pier 7", OrganizationUnitId.CreateSequential(), Owner, Now);

    /// <summary>The events the project raised and still holds, each without what tells one raising from another.</summary>
    private static IEnumerable<DomainEvent> Raised(Project project) => project.DomainEvents.Cast<DomainEvent>().Select(Bare);

    /// <summary>An event without its own id and moment, so two that say the same compare as equal.</summary>
    private static DomainEvent Bare(DomainEvent raised) => raised with { EventId = Guid.Empty, OccurredAt = default };

    /// <summary>The codes of the project's own rules that do not hold, as a save would find them.</summary>
    private static IEnumerable<string> RulesBrokenBy(Project project)
    {
        DDDToolkit.Invariants.IInvariant<Project>[] rules =
        [
            new Project.NumberIsValid(), new Project.NameIsValid(), new Project.OneMembershipPerSeat(),
            new Project.OwnerStaysOnTheCrew(), new Project.PlannedRangeIsValid(),
        ];

        // And each member's own: a role is held once.
        var holds = new CrewMember.OneHoldPerRole();
        return rules.Where(rule => rule.Check(project) is not null).Select(rule => rule.Code)
            .Concat(project.Crew.Where(member => holds.Check(member) is not null).Select(_ => holds.Code));
    }
}
