using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The rules about a resource's members as the member list keeps them, with no storage anywhere: who is a
/// member for which period, which roles each member holds for which period, and what the owner may not lose.
/// Asked through the host's own aggregate, which is how an application meets them: its guard in front, its
/// events behind.
/// </summary>
public sealed class MemberListTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId Owner = UserId.CreateSequential();

    private static readonly UserId Member = UserId.CreateSequential();

    private static readonly NamedRole OwnersRole = DocumentMembership.Owner;

    private static readonly NamedRole Contributor = DocumentMembership.Contributor;

    private static readonly NamedRole Onlooker = DocumentMembership.Onlooker;

    private static readonly MembershipCodes Codes = DocumentRefusals.Membership;

    // ---------------------------------------------------------------- opening

    [Fact]
    public void A_resource_opens_with_its_owner_a_member_for_good_holding_the_owners_role_for_good()
    {
        var document = Written();

        var owner = document.Shares.Should().ContainSingle().Which;
        owner.MemberId.Should().Be(Owner);
        (owner.StartsAt, owner.EndsAt, owner.AddedBy).Should().Be((Now, null, null));
        var held = owner.Roles.Should().ContainSingle().Which;
        (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy).Should().Be((OwnersRole, Now, null, null));
        owner.HoldsAt(OwnersRole, Now).Should().BeTrue();
        owner.HoldsAt(OwnersRole, Now.AddSeconds(-1)).Should().BeFalse("nothing counts before it starts");
        document.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<DocumentWritten>("opening raises nothing of the package's: the host's one event says it all");
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_member_list_is_opened_once()
    {
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>([], Owner, DocumentShareId.CreateSequential, Codes);
        list.Open(OwnersRole, Now);

        FluentActions.Invoking(() => list.Open(OwnersRole, Now)).Should().Throw<InvalidOperationException>().WithMessage("*has members already*");
    }

    [Fact]
    public void A_member_list_is_made_over_the_aggregates_own_collection_its_id_factory_and_its_codes()
    {
        List<DocumentShare> shares = [];

        FluentActions.Invoking(() => new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(null!, Owner, DocumentShareId.CreateSequential, Codes))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, null!, Codes))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, DocumentShareId.CreateSequential, null!))
            .Should().Throw<ArgumentNullException>();

        // It keeps nothing itself: what it adds is in the aggregate's collection, with an id from the factory.
        var id = DocumentShareId.CreateSequential();
        var added = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, () => id, Codes).Add(Member, MemberPeriod.Open(Now), Now);
        shares.Should().ContainSingle().Which.Should().BeSameAs(added);
        added.Id.Should().Be(id);
    }

    // ---------------------------------------------------------------- members and their roles

    [Fact]
    public void A_member_is_added_with_no_role_and_is_given_roles_one_by_one()
    {
        var document = Written();
        var until = Now.AddDays(30);

        var member = document.ShareWith(Member, MemberPeriod.Between(Now, until), Now, by: Owner);
        member.Roles.Should().BeEmpty("being a member and holding a role are two things");
        (member.MemberId, member.StartsAt, member.EndsAt, member.AddedBy).Should().Be((Member, Now, until, Owner));

        var edits = document.GiveRole(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: Owner);
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: null);

        (edits.RoleId, edits.StartsAt, edits.EndsAt, edits.GivenBy).Should().Be((Contributor, Now, Now.AddDays(7), Owner), "giving a role answers the role as it is held");
        member.Roles.Select(held => (held.RoleId, held.EndsAt, held.GivenBy)).Should().Equal((Contributor, Now.AddDays(7), Owner), (Onlooker, null, null));
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_member_added_with_a_role_holds_it_for_the_period_of_the_membership()
    {
        var document = Written();
        var until = Now.AddDays(30);

        var member = document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, until), Now, by: Owner);

        (member.StartsAt, member.EndsAt, member.AddedBy).Should().Be((Now, until, Owner));
        var held = member.Roles.Should().ContainSingle().Which;
        (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy).Should().Be((Contributor, Now, until, Owner));

        // Refused as a whole when the member is one already: no role is given on the way to the refusal.
        var again = Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner));
        again.Arguments.Should().Contain("Member", Member);
        member.Roles.Should().ContainSingle();
    }

    [Fact]
    public void A_role_counts_only_inside_its_membership()
    {
        var document = Written();
        var until = Now.AddDays(30);
        var member = document.ShareWith(Member, MemberPeriod.Between(Now, until), Now, by: Owner);
        document.GiveRole(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: Owner);
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);

        // A role counts while its own period applies and the membership's does.
        member.HoldsAt(Contributor, Now.AddDays(6)).Should().BeTrue();
        member.HoldsAt(Contributor, Now.AddDays(7)).Should().BeFalse("its own period has ended, while the membership goes on");
        member.AppliesAt(Now.AddDays(7)).Should().BeTrue();
        member.HoldsAt(Onlooker, Now.AddDays(29)).Should().BeTrue();
        member.HoldsAt(Onlooker, until).Should().BeFalse("the membership has ended, whatever the role's own dates say");
        member.Roles.Single(held => held.RoleId == Onlooker).AppliesAt(until).Should().BeTrue("the role's own period has no end");
        member.HoldsAt(Onlooker, Now.AddSeconds(-1)).Should().BeFalse("nothing counts before it starts");

        // A role may be given for longer than the membership runs: past the membership's end it counts for nothing.
        var visitor = UserId.CreateSequential();
        var brief = document.ShareWith(visitor, MemberPeriod.Between(Now, Now.AddDays(1)), Now, by: Owner);
        document.GiveRole(visitor, Contributor, MemberPeriod.Between(Now, Now.AddDays(365)), Now, by: Owner);
        brief.HoldsAt(Contributor, Now).Should().BeTrue();
        brief.HoldsAt(Contributor, Now.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void A_member_holds_a_role_once_one_still_held_is_refused_and_one_that_has_ended_is_replaced()
    {
        var document = Written();
        document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner);
        document.GiveRole(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: Owner);

        // While the first has not ended when the second is given: held, whether the second would start now or later.
        foreach (var moment in new[] { Now, Now.AddDays(6), Now.AddDays(7).AddTicks(-1) })
        {
            var refusal = Refused.With(Codes, MembershipRefusals.RoleHeld, () => document.GiveRole(Member, Contributor, MemberPeriod.Open(moment), moment, by: Owner));
            refusal.Arguments.Should().Contain("Member", Member).And.Contain("Role", Contributor);
        }

        // Once it has ended, the role is given again, and the new hold takes the old one's place.
        document.GiveRole(Member, Contributor, MemberPeriod.Open(Now.AddDays(7)), Now.AddDays(7), by: null);

        var held = document.Shares.Single(share => share.MemberId == Member).Roles.Should().ContainSingle().Which;
        (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy).Should().Be((Contributor, Now.AddDays(7), null, null));
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_role_that_still_runs_is_not_replaced_by_one_that_starts_after_it_ends()
    {
        // The tenth of January. The role runs until the first of February, on a membership with no end.
        var today = new DateTimeOffset(2027, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var february = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var march = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var document = Written();
        var member = document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner);
        document.GiveRole(Member, Contributor, MemberPeriod.Between(Now, february), Now, by: Owner);

        // Given again from March, today: the hold that runs is the member's one hold of the role, so this is
        // refused. Judged by the new period's start, the running hold would be gone from today until March.
        Refused.With(Codes, MembershipRefusals.RoleHeld, () => document.GiveRole(Member, Contributor, MemberPeriod.Open(march), today, by: Owner));

        member.HoldsAt(Contributor, today).Should().BeTrue("what runs today is not over because something else is to start in March");
        member.Roles.Should().ContainSingle().Which.EndsAt.Should().Be(february);

        // A hold that is still to start is the one hold as well, whatever start the next would have.
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(march), today, by: Owner);
        Refused.With(Codes, MembershipRefusals.RoleHeld, () => document.GiveRole(Member, Onlooker, MemberPeriod.Open(today), today, by: Owner));

        // On the first of February the first hold is over, and the role is given again.
        document.GiveRole(Member, Contributor, MemberPeriod.Open(march), february, by: Owner);
        member.Roles.Single(held => held.RoleId == Contributor).StartsAt.Should().Be(march);
    }

    [Fact]
    public void A_role_is_given_again_once_it_has_ended_whatever_start_is_said()
    {
        var document = Written();
        var member = document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner);
        document.GiveRole(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(5)), Now, by: Owner);
        var today = Now.AddDays(10);

        // The hold ended five days ago. Given again with a start before that end, it is still given: what
        // decides is that the hold is over today, not where the new period is said to start.
        var again = document.GiveRole(Member, Contributor, MemberPeriod.Open(Now.AddDays(1)), today, by: Owner);

        member.Roles.Should().ContainSingle().Which.Should().BeSameAs(again);
        (again.StartsAt, again.EndsAt).Should().Be((Now.AddDays(1), null));
    }

    [Fact]
    public void Taking_a_role_leaves_the_member_and_refuses_what_is_not_there()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Open(Now), Now, by: Owner);

        document.TakeRole(Member, Contributor, OwnersRole);

        document.Shares.Single(share => share.MemberId == Member).Roles.Should().BeEmpty("the member stays, without the role");

        var again = Refused.With(Codes, MembershipRefusals.RoleNotHeld, () => document.TakeRole(Member, Contributor, OwnersRole));
        again.Arguments.Should().Contain("Member", Member).And.Contain("Role", Contributor);

        var stranger = UserId.CreateSequential();
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.TakeRole(stranger, Contributor, OwnersRole)).Arguments.Should().Contain("Member", stranger);
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.GiveRole(stranger, Contributor, MemberPeriod.Open(Now), Now, by: Owner));
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.Unshare(stranger));
    }

    [Fact]
    public void A_role_can_be_taken_from_a_member_whose_membership_has_ended()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(1)), Now, by: Owner);

        // What was given once can always be taken back, and so can the member itself.
        document.TakeRole(Member, Contributor, OwnersRole);
        document.Unshare(Member);

        document.Shares.Should().ContainSingle().Which.MemberId.Should().Be(Owner);
    }

    [Fact]
    public void Removing_a_member_takes_every_role_it_holds_with_it()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Open(Now), Now, by: Owner);
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);

        document.Unshare(Member);

        document.Shares.Select(share => share.MemberId).Should().Equal(Owner);

        // Back again, the member holds nothing: the roles went with the membership.
        document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner).Roles.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- an ended membership

    [Fact]
    public void An_ended_membership_begins_again_without_its_roles()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(30)), Now, by: Owner);
        var later = Now.AddDays(90);

        // A role counts only while the membership does, so giving one to a member whose membership ended would
        // give nothing: it is refused, rather than answered as if it had worked.
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.GiveRole(Member, Onlooker, MemberPeriod.Open(later), later, by: Owner));

        // Added again, it has a new membership, without the role of the one that ended.
        var again = document.ShareWith(Member, MemberPeriod.Open(later), later, by: Owner);
        document.Shares.Where(share => share.MemberId == Member).Should().ContainSingle().Which.Should().BeSameAs(again);
        (again.StartsAt, again.EndsAt).Should().Be((later, null));
        again.Roles.Should().BeEmpty();
        again.HoldsAt(Contributor, later).Should().BeFalse();
        document.GetInvariantViolations().Should().BeEmpty();

        // While a membership runs, or is yet to start, the member is on the list already.
        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Member, MemberPeriod.Open(later.AddDays(1)), later, by: Owner));
        var early = UserId.CreateSequential();
        document.ShareWith(early, MemberPeriod.Open(later.AddDays(10)), later, by: Owner);
        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(early, MemberPeriod.Open(later), later, by: Owner));
    }

    [Fact]
    public void A_membership_is_over_at_the_very_moment_it_ends()
    {
        var document = Written();
        var end = Now.AddDays(30);
        document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, end), Now, by: Owner);

        // An end is the first moment a membership no longer counts: a tick before, the member is one still;
        // at it, the member is added again, and meets nothing.
        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Member, MemberPeriod.Open(end), end.AddTicks(-1), by: Owner));
        document.ShareWith(Member, MemberPeriod.Open(end), end, by: Owner).Roles.Should().BeEmpty();
    }

    [Fact]
    public void A_membership_that_still_runs_is_not_replaced_by_one_that_starts_after_it_ends()
    {
        // The tenth of January. Dee is a member until the first of February.
        var today = new DateTimeOffset(2027, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var february = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var march = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var document = Written();
        var dee = document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, february), Now, by: Owner);

        // Somebody adds her from March, today. The membership that runs is her one membership, so this is
        // refused: judged by the new period's start, she would be off the document from today until March.
        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Member, MemberPeriod.Open(march), today, by: Owner));
        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Member, Onlooker, MemberPeriod.Between(march, march.AddDays(30)), today, by: Owner));

        document.Shares.Single(share => share.MemberId == Member).Should().BeSameAs(dee);
        dee.AppliesAt(today).Should().BeTrue("what runs today is not over because something else is to start in March");
        dee.HoldsAt(Contributor, today).Should().BeTrue();
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_member_whose_membership_ended_comes_back_whatever_start_is_said()
    {
        // Dee's membership ended on the 5th of January. It is the tenth.
        var first = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ended = new DateTimeOffset(2027, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var today = new DateTimeOffset(2027, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var document = Written();
        var before = document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, ended), Now, by: Owner);

        // A role given from the first of January would land on the membership that is over, and give nothing: refused.
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.GiveRole(Member, Onlooker, MemberPeriod.Open(first), today, by: Owner));

        // Added from the first of January, she comes back: the membership she had is over today, which is
        // what decides, not that the new one is said to have started before the old one ended.
        var back = document.ShareWith(Member, MemberPeriod.Open(first), today, by: Owner);

        document.Shares.Where(share => share.MemberId == Member).Should().ContainSingle().Which.Should().BeSameAs(back).And.NotBeSameAs(before);
        (back.StartsAt, back.EndsAt).Should().Be((first, null));
        back.Roles.Should().BeEmpty("the roles of the membership that ended do not come back");
    }

    // ---------------------------------------------------------------- the owner

    [Fact]
    public void The_owner_stays_with_no_end_in_the_owners_role()
    {
        var document = Written();
        document.GiveRole(Owner, Contributor, MemberPeriod.Open(Now), Now, by: Owner);
        document.GiveRole(Owner, Onlooker, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: Owner);

        // The owner's role, as the use case named it, is the owner's until another owner is named.
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => document.TakeRole(Owner, OwnersRole, OwnersRole)).Arguments.Should().BeEmpty();

        // With no owner's role in use any more, the use case has none to name. The owner then still keeps a
        // role that does not run out: the last one cannot be taken, and a dated one does not count as it.
        document.TakeRole(Owner, Contributor, ownerRole: null);
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => document.TakeRole(Owner, OwnersRole, ownerRole: null));

        // What is not the owner's role and not the last is the owner's to lose, like anyone's.
        document.TakeRole(Owner, Onlooker, OwnersRole);
        document.Shares.Single().Roles.Select(held => held.RoleId).Should().Equal(OwnersRole);

        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => document.Unshare(Owner));

        // And the owner is a member already, for good: adding them again is refused, whatever the period.
        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Owner, MemberPeriod.Open(Now.AddYears(10)), Now, by: Member));
        var owner = document.Shares.Single();
        (owner.EndsAt, owner.Roles.Single().EndsAt).Should().Be((null, null));
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void Somebody_who_holds_the_owners_role_without_being_the_owner_loses_it_like_any_other_role()
    {
        var document = Written();
        document.ShareWith(Member, OwnersRole, MemberPeriod.Open(Now), Now, by: Owner);

        // Holding everything the owner holds does not make a member the owner: only the owner is protected.
        document.TakeRole(Member, OwnersRole, OwnersRole);
        document.Unshare(Member);

        document.Shares.Should().ContainSingle().Which.MemberId.Should().Be(Owner);
    }

    [Fact]
    public void Naming_an_owner_who_is_a_member_keeps_their_place_for_good_and_takes_the_old_owners_role_alone()
    {
        var document = Written();
        document.GiveRole(Owner, Contributor, MemberPeriod.Open(Now), Now, by: Owner);
        document.ShareWith(Member, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: Owner);
        document.GiveRole(Member, OwnersRole, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: Owner);
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        Forget(document);
        var later = Now.AddDays(1);

        document.HandOver(Member, OwnersRole, later);

        // The new owner was a member, and held the owner's role, for a week: both are for good now. The
        // onlooker's role had no end of its own and counted for that week: it still ends there. The old owner
        // lost the owner's role and kept the contributor's, and their place.
        document.OwnerId.Should().Be(Member);
        var newOwner = document.Shares.Single(share => share.MemberId == Member);
        (newOwner.StartsAt, newOwner.EndsAt).Should().Be((Now, null));
        newOwner.Roles.Select(held => (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy))
            .Should().BeEquivalentTo([(Onlooker, Now, (DateTimeOffset?)Now.AddDays(7), (UserId?)Owner), (OwnersRole, later, null, null)]);
        var oldOwner = document.Shares.Single(share => share.MemberId == Owner);
        oldOwner.EndsAt.Should().BeNull();
        oldOwner.Roles.Select(held => held.RoleId).Should().Equal(Contributor);
        Raised(document).Should().Equal(
            Bare(new DocumentRoleGiven(document.Id, Member, OwnersRole)),
            Bare(new DocumentRoleTaken(document.Id, Owner, OwnersRole)),
            Bare(new DocumentHandedOver(document.Id, Owner, Member)));
        document.GetInvariantViolations().Should().BeEmpty();

        // Named again, the member is the owner already; named back, the first owner is given the role anew.
        Refused.With(Codes, MembershipRefusals.AlreadyOwner, () => document.HandOver(Member, OwnersRole, later)).Arguments.Should().BeEmpty();
        document.HandOver(Owner, OwnersRole, later);
        document.Shares.Single(share => share.MemberId == Owner).Roles.Select(held => held.RoleId).Should().BeEquivalentTo([Contributor, OwnersRole]);
        document.Shares.Single(share => share.MemberId == Member).Roles.Select(held => held.RoleId).Should().Equal(Onlooker);
    }

    [Fact]
    public void A_member_for_a_week_who_owned_the_resource_for_a_day_stays_for_good_and_holds_its_other_roles_no_longer_than_the_week()
    {
        List<DocumentShare> shares = [];
        var week = Now.AddDays(7);
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, DocumentShareId.CreateSequential, Codes);
        list.Open(OwnersRole, Now);

        // Dee is on the document for a week: an onlooker with no end of its own, a contributor for a fortnight,
        // which counts for the week alone, and for three days something that runs out before the week does.
        var dee = list.Add(Member, MemberPeriod.Between(Now, week), Now, by: Owner);
        list.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        list.GiveRole(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(14)), Now, by: Owner);
        var brief = new NamedRole("brief");
        list.GiveRole(Member, brief, MemberPeriod.Between(Now, Now.AddDays(3)), Now, by: Owner);
        var late = new NamedRole("late");
        list.GiveRole(Member, late, MemberPeriod.Open(week.AddDays(1)), Now, by: Owner);

        // Named owner on the first day.
        var named = list.NameOwner(Member, OwnersRole, Now.AddDays(1));

        // The answer says the end that went, so a host that wants her off again at that moment can keep it.
        named.EndLifted.Should().Be(week);
        (named.Added, named.BeganAnew, named.StartMoved, named.RoleGiven, named.RoleTaken).Should().Be((false, false, null, true, true));
        named.RolesDropped.Select(held => held.RoleId).Should().Equal([late], "a role that was to start when the membership would have been over never counted");

        // Handed back on the second day, by the aggregate, which keeps who the owner is.
        var handedBack = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Member, DocumentShareId.CreateSequential, Codes)
            .NameOwner(Owner, OwnersRole, Now.AddDays(2));
        handedBack.EndLifted.Should().BeNull("the first owner's place had no end");

        // An owner's place has no end, and stays so: she is on the document for good.
        (dee.StartsAt, dee.EndsAt).Should().Be((Now, null));
        dee.AppliesAt(Now.AddYears(5)).Should().BeTrue();

        // What her roles gave, they give no longer than they ever did: until the week was to end.
        dee.Roles.Select(held => (held.RoleId, held.EndsAt)).Should().BeEquivalentTo(
            [(Onlooker, (DateTimeOffset?)week), (Contributor, (DateTimeOffset?)week), (brief, (DateTimeOffset?)Now.AddDays(3))]);
        dee.HoldsAt(Onlooker, week.AddTicks(-1)).Should().BeTrue();
        dee.HoldsAt(Onlooker, week).Should().BeFalse("a role with no end of its own got the end the membership had");
        dee.HoldsAt(Contributor, week).Should().BeFalse("a role that ran past the membership's end stops where it always did");
        dee.HoldsAt(OwnersRole, Now.AddDays(3)).Should().BeFalse("the owner's role went with the ownership");
    }

    [Fact]
    public void Naming_an_owner_whose_membership_had_ended_says_that_it_began_anew_and_which_roles_went()
    {
        List<DocumentShare> shares = [];
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, DocumentShareId.CreateSequential, Codes);
        list.Open(OwnersRole, Now);
        list.Add(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(30)), Now, by: Owner);
        list.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        var later = Now.AddDays(90);

        var named = list.NameOwner(Member, OwnersRole, later);

        // Not added: the member was on the list. But nothing of the membership that ended is left, and the
        // answer says so, so an aggregate that raises an event for every role a member loses can raise them.
        (named.Added, named.BeganAnew, named.RoleGiven, named.RoleTaken).Should().Be((false, true, true, true));
        named.RolesDropped.Select(held => (held.RoleId, held.StartsAt, held.EndsAt))
            .Should().Equal((Contributor, Now, Now.AddDays(30)), (Onlooker, Now, null));
        (named.EndLifted, named.StartMoved).Should().Be((null, null), "the membership that ended is over: nothing of it was changed");

        // And the host's aggregate raises its own events from that answer.
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(30)), Now, by: Owner);
        Forget(document);

        document.HandOver(Member, OwnersRole, later);

        Raised(document).Should().Equal(
            Bare(new DocumentRoleTaken(document.Id, Member, Contributor)),
            Bare(new DocumentShared(document.Id, Member)),
            Bare(new DocumentRoleGiven(document.Id, Member, OwnersRole)),
            Bare(new DocumentRoleTaken(document.Id, Owner, OwnersRole)),
            Bare(new DocumentHandedOver(document.Id, Owner, Member)));
    }

    [Fact]
    public void Naming_an_owner_who_already_holds_the_owners_role_for_good_gives_nothing_again()
    {
        var document = Written();
        document.ShareWith(Member, OwnersRole, MemberPeriod.Open(Now), Now, by: Owner);
        Forget(document);

        document.HandOver(Member, OwnersRole, Now.AddDays(1));

        var held = document.Shares.Single(share => share.MemberId == Member).Roles.Should().ContainSingle().Which;
        (held.StartsAt, held.GivenBy).Should().Be((Now, Owner), "the role the member held is the one it keeps");
        Raised(document).Should().Equal(
            Bare(new DocumentRoleTaken(document.Id, Owner, OwnersRole)),
            Bare(new DocumentHandedOver(document.Id, Owner, Member)));
    }

    [Fact]
    public void Naming_an_owner_whose_membership_had_ended_starts_it_anew_with_the_owners_role_alone()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(30)), Now, by: Owner);
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        var later = Now.AddDays(90);

        // The membership ran out two months ago, and the onlooker's role, which had no end of its own, with it.
        // Named owner, the member is one from now on: nobody gave it the onlooker's role again.
        document.HandOver(Member, OwnersRole, later);

        var member = document.Shares.Single(share => share.MemberId == Member);
        (member.StartsAt, member.EndsAt).Should().Be((later, null), "the membership does not read as unbroken since it first began");
        member.Roles.Select(held => (held.RoleId, held.StartsAt, held.EndsAt)).Should().Equal((OwnersRole, later, null));
        member.HoldsAt(Onlooker, later).Should().BeFalse();
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void Naming_an_owner_whose_membership_is_yet_to_start_starts_it_now()
    {
        var document = Written();
        document.ShareWith(Member, Onlooker, MemberPeriod.Between(Now.AddDays(10), Now.AddDays(20)), Now, by: Owner);

        document.HandOver(Member, OwnersRole, Now.AddDays(1));

        var member = document.Shares.Single(share => share.MemberId == Member);
        (member.StartsAt, member.EndsAt).Should().Be((Now.AddDays(1), null), "an owner is a member from the moment it is named");
        member.Roles.Select(held => held.RoleId).Should().BeEquivalentTo([Onlooker, OwnersRole], "a membership that had not ended keeps what it holds");
        member.HoldsAt(OwnersRole, Now.AddDays(1)).Should().BeTrue();
        member.Roles.Single(held => held.RoleId == Onlooker).Should().Match<MemberRole<UserId, NamedRole>>(
            held => held.StartsAt == Now.AddDays(10) && held.EndsAt == Now.AddDays(20),
            "a role with dates of its own inside the membership's keeps them");

        // The answer says both changes of the period: the start that was moved, and the end that went.
        List<DocumentShare> shares = [];
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, DocumentShareId.CreateSequential, Codes);
        list.Open(OwnersRole, Now);
        list.Add(Member, MemberPeriod.Between(Now.AddDays(10), Now.AddDays(20)), Now, by: Owner);

        var named = list.NameOwner(Member, OwnersRole, Now.AddDays(1));

        (named.StartMoved, named.EndLifted, named.BeganAnew, named.Added).Should().Be((Now.AddDays(10), Now.AddDays(20), false, false));
        named.RolesDropped.Should().BeEmpty();
    }

    [Fact]
    public void A_member_who_was_to_come_later_and_owned_the_resource_before_then_holds_its_other_roles_no_sooner_than_it_was_to()
    {
        List<DocumentShare> shares = [];
        var (comes, leaves) = (Now.AddDays(10), Now.AddDays(20));
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, DocumentShareId.CreateSequential, Codes);
        list.Open(OwnersRole, Now);

        // Dee is to be on the document from the tenth day to the twentieth. Her roles have dates of their own:
        // one given from today on, which counts only once she is there; one inside her time; and one that is
        // over before she comes, which never counts.
        var dee = list.Add(Member, MemberPeriod.Between(comes, leaves), Now, by: Owner);
        list.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        list.GiveRole(Member, Contributor, MemberPeriod.Between(Now.AddDays(12), Now.AddDays(15)), Now, by: Owner);
        var early = new NamedRole("early");
        list.GiveRole(Member, early, MemberPeriod.Between(Now, Now.AddDays(5)), Now, by: Owner);
        dee.HoldsAt(Onlooker, Now.AddDays(2)).Should().BeFalse("a role counts only inside its membership, which has not started");

        // Named owner on the first day, and the document handed back on the second.
        var named = list.NameOwner(Member, OwnersRole, Now.AddDays(1));
        new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Member, DocumentShareId.CreateSequential, Codes)
            .NameOwner(Owner, OwnersRole, Now.AddDays(2));

        (named.StartMoved, named.EndLifted).Should().Be((comes, leaves));
        named.RolesDropped.Select(held => held.RoleId).Should().Equal([early], "a role that was over before the membership was to start never counted");

        // She is on the document from the day she was named, for good: an owner's place.
        (dee.StartsAt, dee.EndsAt).Should().Be((Now.AddDays(1), null));

        // Her roles give nothing sooner, and nothing for longer, than they were to: each kept to the period her membership had.
        dee.Roles.Select(held => (held.RoleId, held.StartsAt, held.EndsAt)).Should().BeEquivalentTo(
            [(Onlooker, comes, (DateTimeOffset?)leaves), (Contributor, Now.AddDays(12), (DateTimeOffset?)Now.AddDays(15))]);
        dee.HoldsAt(Onlooker, Now.AddDays(3)).Should().BeFalse("she was not to look on before the tenth day, and passing through ownership gave her no head start");
        dee.HoldsAt(Onlooker, comes).Should().BeTrue();
        dee.HoldsAt(Onlooker, leaves).Should().BeFalse();

        // A membership with no end that was to start later: the same for the start alone.
        var cy = UserId.CreateSequential();
        var later = list.Add(cy, MemberPeriod.Open(comes), Now, by: Owner);
        list.GiveRole(cy, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        var again = list.NameOwner(cy, OwnersRole, Now.AddDays(3));

        (again.StartMoved, again.EndLifted).Should().Be((comes, null));
        later.Roles.Single(held => held.RoleId == Onlooker).Should().Match<MemberRole<UserId, NamedRole>>(held => held.StartsAt == comes && held.EndsAt == null);
        later.HoldsAt(OwnersRole, Now.AddDays(3)).Should().BeTrue("the owner's role is the owner's from the moment it is named");
    }

    [Fact]
    public void Naming_an_owner_who_is_no_member_adds_them()
    {
        var document = Written();
        Forget(document);

        document.HandOver(Member, OwnersRole, Now.AddDays(1));

        document.Shares.Select(share => (share.MemberId, Roles: share.Roles.Count)).Should().Equal((Owner, 0), (Member, 1));
        document.Shares.Single(share => share.MemberId == Member).AddedBy.Should().BeNull("nobody added the owner: they were named");
        Raised(document).Should().Equal(
            Bare(new DocumentShared(document.Id, Member)),
            Bare(new DocumentRoleGiven(document.Id, Member, OwnersRole)),
            Bare(new DocumentRoleTaken(document.Id, Owner, OwnersRole)),
            Bare(new DocumentHandedOver(document.Id, Owner, Member)));

        // The old owner is a member like any other now, with no role, and can be removed.
        document.Unshare(Owner);
        document.Shares.Should().ContainSingle().Which.MemberId.Should().Be(Member);
    }

    [Fact]
    public void Naming_an_owner_answers_what_happened_for_the_aggregate_to_act_on()
    {
        List<DocumentShare> shares = [];
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, DocumentShareId.CreateSequential, Codes);
        list.Open(OwnersRole, Now);

        var named = list.NameOwner(Member, OwnersRole, Now.AddDays(1));

        (named.Previous, named.Owner, named.Added, named.RoleGiven, named.RoleTaken).Should().Be((Owner, Member, true, true, true));
        (named.BeganAnew, named.EndLifted, named.StartMoved).Should().Be((false, null, null), "somebody who was no member had no membership to change");
        named.RolesDropped.Should().BeEmpty();

        // The list does not keep who the owner is: until the aggregate does, the list still protects the old one.
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => list.Remove(Owner));
        var kept = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, named.Owner, DocumentShareId.CreateSequential, Codes);
        kept.Remove(Owner).MemberId.Should().Be(Owner);
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => kept.Remove(Member));
    }

    // ---------------------------------------------------------------- periods

    [Fact]
    public void A_period_that_ends_at_or_before_its_start_is_refused_and_nothing_changes()
    {
        var document = Written();
        document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner);
        var newcomer = UserId.CreateSequential();

        foreach (var end in new[] { Now, Now.AddDays(-1) })
        {
            var empty = MemberPeriod.Between(Now, end);
            empty.IsEmpty.Should().BeTrue();

            var added = Refused.With(Codes, MembershipRefusals.InvalidPeriod, () => document.ShareWith(newcomer, empty, Now, by: Owner));
            added.Arguments[RefusalException.FieldArgument].Should().Be("until", "an Invalid refusal names the input it is about");
            Refused.With(Codes, MembershipRefusals.InvalidPeriod, () => document.ShareWith(newcomer, Contributor, empty, Now, by: Owner));
            Refused.With(Codes, MembershipRefusals.InvalidPeriod, () => document.GiveRole(Member, Contributor, empty, Now, by: Owner));
        }

        document.Shares.Select(share => share.MemberId).Should().Equal(Owner, Member);
        document.Shares.Single(share => share.MemberId == Member).Roles.Should().BeEmpty();

        // The period is refused before the member is looked at: a wrong end is a wrong request, whoever it names.
        Refused.With(Codes, MembershipRefusals.InvalidPeriod, () => document.ShareWith(Member, MemberPeriod.Between(Now, Now), Now, by: Owner));
        Refused.With(Codes, MembershipRefusals.InvalidPeriod, () => document.GiveRole(newcomer, Contributor, MemberPeriod.Between(Now, Now), Now, by: Owner));
    }

    [Fact]
    public void A_period_counts_from_its_start_until_its_end()
    {
        var open = MemberPeriod.Open(Now);
        (open.Starts, open.Ends, open.IsEmpty).Should().Be((Now, null, false));
        open.AppliesAt(Now).Should().BeTrue();
        open.AppliesAt(Now.AddTicks(-1)).Should().BeFalse();
        open.AppliesAt(DateTimeOffset.MaxValue).Should().BeTrue();

        var week = MemberPeriod.Between(Now, Now.AddDays(7));
        week.AppliesAt(Now.AddDays(7).AddTicks(-1)).Should().BeTrue();
        week.AppliesAt(Now.AddDays(7)).Should().BeFalse("an end is the first moment it no longer counts");
        MemberPeriod.Between(Now, null).Should().Be(open, "no end is no end, however it is said");
    }

    // ---------------------------------------------------------------- the host's guard, and its events

    [Fact]
    public void The_hosts_own_guard_runs_in_front_of_every_change()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Open(Now), Now, by: Owner);
        document.MoveToArchive();

        Action[] changes =
        [
            () => document.ShareWith(UserId.CreateSequential(), MemberPeriod.Open(Now), Now, by: Owner),
            () => document.ShareWith(UserId.CreateSequential(), Onlooker, MemberPeriod.Open(Now), Now, by: Owner),
            () => document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner),
            () => document.TakeRole(Member, Contributor, OwnersRole),
            () => document.Unshare(Member),
            () => document.HandOver(Member, OwnersRole, Now),
        ];

        foreach (var change in changes)
        {
            // The resource's own code, not one of the package's: when a resource changes no more is its business.
            change.Should().Throw<RefusalException>().Which.Code.Should().Be(DocumentRefusals.Archived);
        }

        document.Shares.Should().HaveCount(2);
        document.OwnerId.Should().Be(Owner);
    }

    [Fact]
    public void The_host_raises_its_own_events_from_what_each_change_answers()
    {
        var document = Written();
        Forget(document);

        document.ShareWith(Member, Contributor, MemberPeriod.Open(Now), Now, by: Owner);
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        document.TakeRole(Member, Contributor, OwnersRole);
        document.Unshare(Member);

        // The package raises none of its own, so names and payloads are the host's to choose and to keep.
        Raised(document).Should().Equal(
            Bare(new DocumentShared(document.Id, Member)),
            Bare(new DocumentRoleGiven(document.Id, Member, Contributor)),
            Bare(new DocumentRoleGiven(document.Id, Member, Onlooker)),
            Bare(new DocumentRoleTaken(document.Id, Member, Contributor)),
            Bare(new DocumentUnshared(document.Id, Member)));
        document.DomainEvents.Should().OnlyContain(raised => raised.GetType().Namespace == typeof(Document).Namespace);
    }

    [Fact]
    public void A_refused_change_changes_nothing_and_raises_nothing()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Open(Now), Now, by: Owner);
        Forget(document);

        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner));
        Refused.With(Codes, MembershipRefusals.RoleHeld, () => document.GiveRole(Member, Contributor, MemberPeriod.Open(Now), Now, by: Owner));
        Refused.With(Codes, MembershipRefusals.RoleNotHeld, () => document.TakeRole(Member, Onlooker, OwnersRole));
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => document.Unshare(Owner));
        Refused.With(Codes, MembershipRefusals.AlreadyOwner, () => document.HandOver(Owner, OwnersRole, Now));

        document.DomainEvents.Should().BeEmpty();
        document.Shares.Select(share => (share.MemberId, Roles: share.Roles.Count)).Should().Equal((Owner, 1), (Member, 1));
    }

    // ---------------------------------------------------------------- the net under the methods

    [Fact]
    public void A_member_that_is_on_the_list_twice_is_caught_under_the_resources_own_code()
    {
        var document = Written();
        document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner);

        // Went round the member list: the row of another document's member, for the same user.
        var elsewhere = new Document(DocumentId.CreateSequential(), "Elsewhere", Member, OwnersRole, Now);
        Break.ListOf<DocumentShare>(document, "_shares").Add(elsewhere.Shares.Single());

        var violation = document.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("documents.already-member");
        violation.EntityType.Should().Be<Document>();
        violation.Arguments.Should().Contain("Member", Member);
        violation.Message.Should().Be(MembershipRefusals.TemplateOf(MembershipRefusals.AlreadyMember), "the rule reports the text of the refusal, so one translation serves both");
    }

    [Theory]
    [InlineData("the membership ends")]
    [InlineData("every role ends")]
    [InlineData("no role is held")]
    [InlineData("the owner is no member")]
    public void An_owner_whose_place_or_whose_roles_run_out_is_caught_under_the_resources_own_code(string broken)
    {
        var document = Written();
        var owner = document.Shares.Single();
        switch (broken)
        {
            case "the membership ends":
                Break.Set(owner, nameof(DocumentShare.EndsAt), (DateTimeOffset?)Now.AddDays(30));
                break;
            case "every role ends":
                Break.Set(owner.Roles.Single(), nameof(MemberRole<UserId, NamedRole>.EndsAt), (DateTimeOffset?)Now.AddDays(30));
                break;
            case "no role is held":
                Break.ListOf<MemberRole<UserId, NamedRole>>(owner, "_roles").Clear();
                break;
            default:
                Break.ListOf<DocumentShare>(document, "_shares").Clear();
                break;
        }

        var violation = document.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("documents.owner-protected");
        violation.Message.Should().Be(MembershipRefusals.TemplateOf(MembershipRefusals.OwnerProtected));
    }

    [Fact]
    public void A_role_a_member_holds_twice_is_caught_by_the_member_itself()
    {
        var document = Written();
        var member = document.ShareWith(Member, Contributor, MemberPeriod.Open(Now), Now, by: Owner);

        // Went round the member list: a second hold of the role the member has, taken from another document.
        var elsewhere = new Document(DocumentId.CreateSequential(), "Elsewhere", Member, OwnersRole, Now);
        var second = elsewhere.ShareWith(UserId.CreateSequential(), Contributor, MemberPeriod.Open(Now), Now, by: Member).Roles.Single();
        Break.ListOf<MemberRole<UserId, NamedRole>>(member, "_roles").Add(second);

        // The member is the one that is wrong, and it reports the package's own name for the rule: a member
        // does not know which resource it is a member of.
        var violation = document.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be(MembershipRefusals.RoleHeld);
        violation.EntityType.Should().Be<DocumentShare>();
        violation.EntityId.Should().Be(member.Id);
        violation.Arguments.Should().Contain("Member", Member).And.Contain("Role", Contributor);
        member.GetOwnInvariantViolations().Should().ContainSingle().Which.Code.Should().Be(MembershipRefusals.RoleHeld);
        FluentActions.Invoking(document.EnsureInvariants).Should().Throw<InvariantViolationException>();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A document written now, owned by <see cref="Owner"/>, who holds the owner's role on it.</summary>
    private static Document Written() => new(DocumentId.CreateSequential(), "Minutes", Owner, OwnersRole, Now);

    /// <summary>Forgets the events raised so far, so a test reads only what its own change raised.</summary>
    private static void Forget(Document document) => ((IHasDomainEvents)document).DequeueDomainEvents();

    /// <summary>The events the document raised and still holds, each without what tells one raising from another.</summary>
    private static IEnumerable<DomainEvent> Raised(Document document) => document.DomainEvents.Cast<DomainEvent>().Select(Bare);

    /// <summary>An event without its own id and moment, so two that say the same compare as equal.</summary>
    private static DomainEvent Bare(DomainEvent raised) => raised with { EventId = Guid.Empty, OccurredAt = default };
}
