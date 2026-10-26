using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// The crew's rules as the project itself keeps them, with no host and no database: who is on the crew for which
/// period, which roles each member holds for which period, and what the owner may not lose. The scenarios walk the
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
    public void A_project_opens_with_its_owner_on_the_crew_for_good_holding_the_lead_role_for_good()
    {
        var project = Opened();

        var owner = project.Crew.Should().ContainSingle().Which;
        owner.MemberId.Should().Be(Owner);
        (owner.StartsAt, owner.EndsAt, owner.AddedBy).Should().Be((Now, null, null));
        var held = owner.Roles.Should().ContainSingle().Which;
        (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy).Should().Be((Lead, Now, null, null));
        owner.HoldsAt(Lead, Now).Should().BeTrue();
        owner.HoldsAt(Lead, Now.AddSeconds(-1)).Should().BeFalse("nothing counts before it starts");
        project.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProjectOpened>("opening is one event: the owner's place is part of it");
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void A_seat_goes_on_the_crew_with_no_role_and_is_given_roles_one_by_one()
    {
        var project = Opened();
        var until = Now.AddDays(30);

        var member = project.AddToCrew(Member, MemberPeriod.Between(Now, until), Now, addedBy: Owner);
        member.Roles.Should().BeEmpty("being on the crew and holding a role on it are two things");
        (member.StartsAt, member.EndsAt, member.AddedBy).Should().Be((Now, until, Owner));

        project.GiveCrewRole(Member, Surveyor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, givenBy: Owner);
        project.GiveCrewRole(Member, Observer, MemberPeriod.Open(Now), Now, givenBy: Owner);

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
        project.AddToCrew(Member, MemberPeriod.Open(Now), Now, addedBy: Owner);
        project.GiveCrewRole(Member, Surveyor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, givenBy: Owner);

        // While the first has not ended, at the start of the second: held, whether it applies yet or not.
        foreach (var start in new[] { Now, Now.AddDays(6) })
        {
            var twice = () => project.GiveCrewRole(Member, Surveyor, MemberPeriod.Open(start), start, givenBy: Owner);
            var refusal = twice.Should().Throw<RefusalException>().Which;
            refusal.Code.Should().Be(ProjectRefusals.CrewRoleHeld);
            refusal.Arguments.Should().Contain("Seat", Member).And.Contain("Role", Surveyor);
        }

        // Once it has ended, the role is given again, and the new grant takes the old one's place.
        project.GiveCrewRole(Member, Surveyor, MemberPeriod.Open(Now.AddDays(7)), Now.AddDays(7), givenBy: null);

        var held = project.Crew.Single(member => member.MemberId == Member).Roles.Should().ContainSingle().Which;
        (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy).Should().Be((Surveyor, Now.AddDays(7), null, null));
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void Taking_a_role_leaves_the_member_on_the_crew_and_refuses_what_is_not_there()
    {
        var project = Opened();
        project.AddToCrew(Member, MemberPeriod.Open(Now), Now, addedBy: Owner);
        project.GiveCrewRole(Member, Surveyor, MemberPeriod.Open(Now), Now, givenBy: Owner);
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();

        project.TakeCrewRole(Member, Surveyor, Lead);

        project.Crew.Single(member => member.MemberId == Member).Roles.Should().BeEmpty();
        Raised(project).Should().Equal(Bare(new CrewRoleTaken(project.Id, Member, Surveyor)));

        var again = () => project.TakeCrewRole(Member, Surveyor, Lead);
        again.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.CrewRoleNotFound);
        var stranger = () => project.TakeCrewRole(SeatId.CreateSequential(), Surveyor, Lead);
        stranger.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.MemberNotFound);
        var toNobody = () => project.GiveCrewRole(SeatId.CreateSequential(), Surveyor, MemberPeriod.Open(Now), Now, givenBy: Owner);
        toNobody.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.MemberNotFound);
    }

    [Fact]
    public void The_owner_keeps_the_lead_role_and_never_ends_up_without_a_role_for_good()
    {
        var project = Opened();
        project.GiveCrewRole(Owner, Surveyor, MemberPeriod.Open(Now), Now, givenBy: Owner);
        project.GiveCrewRole(Owner, Observer, MemberPeriod.Between(Now, Now.AddDays(7)), Now, givenBy: Owner);

        // The lead role, as the projects' rules named it to the command, is the owner's until another owner is named.
        var lead = () => project.TakeCrewRole(Owner, Lead, Lead);
        lead.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.OwnerProtected);

        // With no crew lead role in use in the tenant any more, the command has none to name. The owner then still
        // keeps a role that does not run out: the last one cannot be taken, and a dated one does not count as it.
        project.TakeCrewRole(Owner, Surveyor, leadRole: null);
        var last = () => project.TakeCrewRole(Owner, Lead, leadRole: null);
        last.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.OwnerProtected);

        // What is not the lead role and not the last is the owner's to lose, like anyone's.
        project.TakeCrewRole(Owner, Observer, Lead);
        project.Crew.Single().Roles.Select(held => held.RoleId).Should().Equal(Lead);

        var removed = () => project.RemoveFromCrew(Owner);
        removed.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.OwnerProtected);
        RulesBrokenBy(project).Should().BeEmpty();
    }

    [Fact]
    public void Naming_an_owner_who_is_on_the_crew_keeps_their_place_for_good_and_takes_the_old_owners_lead_role_alone()
    {
        var project = Opened();
        project.GiveCrewRole(Owner, Surveyor, MemberPeriod.Open(Now), Now, givenBy: Owner);
        project.AddToCrew(Member, MemberPeriod.Between(Now, Now.AddDays(7)), Now, addedBy: Owner);
        project.GiveCrewRole(Member, Lead, MemberPeriod.Between(Now, Now.AddDays(7)), Now, givenBy: Owner);
        project.GiveCrewRole(Member, Observer, MemberPeriod.Open(Now), Now, givenBy: Owner);
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();
        var later = Now.AddDays(1);

        project.ChangeOwner(Member, Lead, later);

        // The new owner was on the crew, and its lead, for a week: the membership is for good now, and so is the
        // lead role, given anew; the observer's role, which counted for that week, still stops where the membership
        // would have. The old owner lost the lead role and kept the surveyor's, and their place.
        project.OwnerSeatId.Should().Be(Member);
        var newOwner = project.Crew.Single(member => member.MemberId == Member);
        (newOwner.StartsAt, newOwner.EndsAt).Should().Be((Now, null));
        newOwner.Roles.Select(held => (held.RoleId, held.StartsAt, held.EndsAt)).Should().BeEquivalentTo([(Observer, Now, (DateTimeOffset?)Now.AddDays(7)), (Lead, later, null)]);
        var oldOwner = project.Crew.Single(member => member.MemberId == Owner);
        oldOwner.EndsAt.Should().BeNull();
        oldOwner.Roles.Select(held => held.RoleId).Should().Equal(Surveyor);
        Raised(project).Should().Equal(
            Bare(new CrewRoleGiven(project.Id, Member, Lead)),
            Bare(new CrewRoleTaken(project.Id, Owner, Lead)),
            Bare(new OwnerChanged(project.Id, Owner, Member)));
        RulesBrokenBy(project).Should().BeEmpty();

        // Named again, the seat is the owner already; named back, the first owner is given the lead role anew.
        var same = () => project.ChangeOwner(Member, Lead, later);
        same.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.AlreadyOwner);
        project.ChangeOwner(Owner, Lead, later);
        project.Crew.Single(member => member.MemberId == Owner).Roles.Select(held => held.RoleId).Should().BeEquivalentTo([Surveyor, Lead]);
        project.Crew.Single(member => member.MemberId == Member).Roles.Select(held => held.RoleId).Should().Equal(Observer);
    }

    [Fact]
    public void Naming_an_owner_whose_membership_had_ended_starts_it_anew_with_the_lead_role_alone()
    {
        var project = Opened();
        project.AddToCrew(Member, MemberPeriod.Between(Now, Now.AddDays(30)), Now, addedBy: Owner);
        project.GiveCrewRole(Member, Surveyor, MemberPeriod.Open(Now), Now, givenBy: Owner);
        var later = Now.AddDays(90);
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();

        // The membership ran out two months ago, and the surveyor's role, which had no end of its own, with it.
        // Named owner, the seat is on the crew from now on: nobody gave it the surveyor's role again.
        project.ChangeOwner(Member, Lead, later);

        var member = project.Crew.Single(candidate => candidate.MemberId == Member);
        (member.StartsAt, member.EndsAt).Should().Be((later, null), "the membership does not read as unbroken since it first began");
        member.Roles.Select(held => (held.RoleId, held.StartsAt, held.EndsAt)).Should().Equal((Lead, later, null));
        member.HoldsAt(Surveyor, later).Should().BeFalse();
        RulesBrokenBy(project).Should().BeEmpty();

        // The role of the membership that ended is taken, and the seat is on the crew again, as one put on it
        // again would be; then it leads, and the owner before does not.
        Raised(project).Should().Equal(
            Bare(new CrewRoleTaken(project.Id, Member, Surveyor)),
            Bare(new CrewMemberAdded(project.Id, Member)),
            Bare(new CrewRoleGiven(project.Id, Member, Lead)),
            Bare(new CrewRoleTaken(project.Id, Owner, Lead)),
            Bare(new OwnerChanged(project.Id, Owner, Member)));
    }

    [Fact]
    public void A_seat_whose_membership_ended_goes_on_the_crew_again_and_takes_no_role_until_then()
    {
        var project = Opened();
        project.AddToCrew(Member, MemberPeriod.Between(Now, Now.AddDays(30)), Now, addedBy: Owner);
        project.GiveCrewRole(Member, Surveyor, MemberPeriod.Open(Now), Now, givenBy: Owner);
        var later = Now.AddDays(90);

        // A role counts only while the membership does, so giving one to a seat whose membership ended would give
        // nothing: it is refused, where it used to answer as if it had worked.
        var given = () => project.GiveCrewRole(Member, Observer, MemberPeriod.Open(later), later, givenBy: Owner);
        given.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.MemberNotFound);

        // Put on the crew again, it has a new membership, without the role of the one that ended.
        var again = project.AddToCrew(Member, MemberPeriod.Open(later), later, addedBy: Owner);
        project.Crew.Where(candidate => candidate.MemberId == Member).Should().ContainSingle().Which.Should().BeSameAs(again);
        (again.StartsAt, again.EndsAt).Should().Be((later, null));
        again.Roles.Should().BeEmpty();
        RulesBrokenBy(project).Should().BeEmpty();

        // While a membership runs, or is yet to start, the seat is on the crew already.
        var twice = () => project.AddToCrew(Member, MemberPeriod.Open(later.AddDays(1)), later, addedBy: Owner);
        twice.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.AlreadyOnCrew);
    }

    [Fact]
    public void Naming_an_owner_who_is_not_on_the_crew_puts_them_on_it()
    {
        var project = Opened();
        ((DDDToolkit.Interfaces.IHasDomainEvents)project).ClearDomainEvents();

        project.ChangeOwner(Member, Lead, Now.AddDays(1));

        project.Crew.Select(member => (member.MemberId, Roles: member.Roles.Count)).Should().Equal((Owner, 0), (Member, 1));
        Raised(project).Should().Equal(
            Bare(new CrewMemberAdded(project.Id, Member)),
            Bare(new CrewRoleGiven(project.Id, Member, Lead)),
            Bare(new CrewRoleTaken(project.Id, Owner, Lead)),
            Bare(new OwnerChanged(project.Id, Owner, Member)));

        // The old owner is a member like any other now, on the crew with no role, and can be taken off it.
        project.RemoveFromCrew(Owner);
        project.Crew.Should().ContainSingle().Which.MemberId.Should().Be(Member);
    }

    [Fact]
    public void A_closed_project_changes_no_crew()
    {
        var project = Opened();
        project.AddToCrew(Member, MemberPeriod.Open(Now), Now, addedBy: Owner);
        project.GiveCrewRole(Member, Surveyor, MemberPeriod.Open(Now), Now, givenBy: Owner);
        project.Close();

        Action[] changes =
        [
            () => project.AddToCrew(SeatId.CreateSequential(), MemberPeriod.Open(Now), Now, addedBy: Owner),
            () => project.GiveCrewRole(Member, Observer, MemberPeriod.Open(Now), Now, givenBy: Owner),
            () => project.TakeCrewRole(Member, Surveyor, Lead),
            () => project.RemoveFromCrew(Member),
            () => project.ChangeOwner(Member, Lead, Now),
        ];

        foreach (var change in changes)
        {
            change.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.Closed);
        }
    }

    /// <summary>A project opened now, owned by <see cref="Owner"/>, who holds <see cref="Lead"/> on its crew.</summary>
    private static Project Opened()
        => new(ProjectId.CreateSequential(), TenantId.CreateSequential(), "P-001", "Pier 7", OrganizationUnitId.CreateSequential(), Owner, Lead, Now);

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
            new Project.OwnerStaysOnTheCrewWithARole(), new Project.PlannedRangeIsValid(),
        ];

        // And each member's own: a role is held once.
        var holds = new CrewMember.OneHoldPerRole();
        return rules.Where(rule => rule.Check(project) is not null).Select(rule => rule.Code)
            .Concat(project.Crew.Where(member => holds.Check(member) is not null).Select(_ => holds.Code));
    }
}
