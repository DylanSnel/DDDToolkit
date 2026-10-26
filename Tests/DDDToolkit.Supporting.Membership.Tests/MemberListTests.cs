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

    private static readonly NamedRole Contributor = DocumentMembership.Contributor;

    private static readonly NamedRole Onlooker = DocumentMembership.Onlooker;

    private static readonly MembershipCodes Codes = DocumentRefusals.Membership;

    // ---------------------------------------------------------------- opening

    [Fact]
    public void A_resource_opens_with_its_owner_a_member_for_good_holding_no_role()
    {
        var document = Written();

        var owner = document.Shares.Should().ContainSingle().Which;
        owner.MemberId.Should().Be(Owner);
        (owner.StartsAt, owner.EndsAt, owner.AddedBy).Should().Be((Now, null, null));
        owner.Roles.Should().BeEmpty("the owner holds what it holds by owning the resource");
        owner.AppliesAt(Now).Should().BeTrue();
        owner.AppliesAt(Now.AddSeconds(-1)).Should().BeFalse("nothing counts before it starts");
        document.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<DocumentWritten>("opening raises nothing of the package's: the host's one event says it all");
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_member_list_is_opened_once()
    {
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>([], Owner, Codes);
        list.Open(Now);

        FluentActions.Invoking(() => list.Open(Now)).Should().Throw<InvalidOperationException>().WithMessage("*has members already*");
    }

    [Fact]
    public void A_member_list_is_made_over_the_aggregates_own_collection_and_its_codes()
    {
        List<DocumentShare> shares = [];

        FluentActions.Invoking(() => new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(null!, Owner, Codes))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, null!))
            .Should().Throw<ArgumentNullException>();

        // It keeps nothing itself: what it adds is in the aggregate's collection.
        var added = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, Codes).Add(Member, MemberPeriod.Open(Now), Now);
        shares.Should().ContainSingle().Which.Should().BeSameAs(added);
    }

    [Fact]
    public void A_new_member_rows_id_is_made_by_the_id_itself_in_time_order()
    {
        List<DocumentShare> shares = [];
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, Codes);

        var first = list.Add(Member, MemberPeriod.Open(Now), Now);
        var second = list.Add(UserId.CreateSequential(), MemberPeriod.Open(Now), Now);

        // DocumentShareId.Create(), which the generator writes for an id over a Guid: a time-ordered id, made in code.
        first.Id.IsEmpty.Should().BeFalse("the id is made before the row is saved");
        first.Id.Value.Version.Should().Be(7);
        second.Id.Should().NotBe(first.Id);
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

        document.TakeRole(Member, Contributor);

        document.Shares.Single(share => share.MemberId == Member).Roles.Should().BeEmpty("the member stays, without the role");

        var again = Refused.With(Codes, MembershipRefusals.RoleNotHeld, () => document.TakeRole(Member, Contributor));
        again.Arguments.Should().Contain("Member", Member).And.Contain("Role", Contributor);

        var stranger = UserId.CreateSequential();
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.TakeRole(stranger, Contributor)).Arguments.Should().Contain("Member", stranger);
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.GiveRole(stranger, Contributor, MemberPeriod.Open(Now), Now, by: Owner));
        Refused.With(Codes, MembershipRefusals.MemberNotFound, () => document.Unshare(stranger));
    }

    [Fact]
    public void A_role_can_be_taken_from_a_member_whose_membership_has_ended()
    {
        var document = Written();
        document.ShareWith(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(1)), Now, by: Owner);

        // What was given once can always be taken back, and so can the member itself.
        document.TakeRole(Member, Contributor);
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
    public void The_owner_stays_for_good_holding_no_role()
    {
        var document = Written();

        // The owner holds what it holds by owning the resource: a role would give it more, or nothing.
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => document.GiveRole(Owner, Contributor, MemberPeriod.Open(Now), Now, by: Owner)).Arguments.Should().BeEmpty();
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => document.Unshare(Owner));

        // And the owner is a member already, for good: adding them again is refused, whatever the period.
        Refused.With(Codes, MembershipRefusals.AlreadyMember, () => document.ShareWith(Owner, MemberPeriod.Open(Now.AddYears(10)), Now, by: Member));
        var owner = document.Shares.Single();
        (owner.EndsAt, owner.Roles.Count).Should().Be((null, 0));
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_role_called_owner_is_a_role_like_any_other()
    {
        var document = Written();
        var called = new NamedRole("owner");
        document.ShareWith(Member, called, MemberPeriod.Open(Now), Now, by: Owner);

        // Nothing about a role's name makes a member the owner: only the owner is protected.
        document.TakeRole(Member, called);
        document.Unshare(Member);

        document.Shares.Should().ContainSingle().Which.MemberId.Should().Be(Owner);
    }

    [Fact]
    public void Naming_an_owner_who_is_a_member_keeps_their_place_for_good_with_no_role_and_the_old_owner_stays_in_the_role_said()
    {
        var document = Written();
        document.ShareWith(Member, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: Owner);
        document.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        document.GiveRole(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(3)), Now, by: Owner);
        Forget(document);
        var later = Now.AddDays(1);

        document.HandOver(Member, FormerOwner.StaysAs(Contributor), later);

        // The new owner was a member for a week: it is one for good now, and holds what it holds by owning the
        // document, so the roles it held went. The old owner stays on, in the role the command said.
        document.OwnerId.Should().Be(Member);
        var newOwner = document.Shares.Single(share => share.MemberId == Member);
        (newOwner.StartsAt, newOwner.EndsAt, newOwner.Roles.Count).Should().Be((Now, null, 0));
        var oldOwner = document.Shares.Single(share => share.MemberId == Owner);
        oldOwner.EndsAt.Should().BeNull();
        oldOwner.Roles.Select(held => (held.RoleId, held.StartsAt, held.EndsAt, held.GivenBy)).Should().Equal((Contributor, later, null, null));
        Raised(document).Should().Equal(
            Bare(new DocumentRoleTaken(document.Id, Member, Onlooker)),
            Bare(new DocumentRoleTaken(document.Id, Member, Contributor)),
            Bare(new DocumentRoleGiven(document.Id, Owner, Contributor)),
            Bare(new DocumentHandedOver(document.Id, Owner, Member)));
        document.GetInvariantViolations().Should().BeEmpty();

        // Named again, the member is the owner already; named back, the first owner's role goes as the second's did.
        Refused.With(Codes, MembershipRefusals.AlreadyOwner, () => document.HandOver(Member, FormerOwner.Leaves, later)).Arguments.Should().BeEmpty();
        document.HandOver(Owner, FormerOwner.StaysAs(Onlooker), later);
        document.Shares.Single(share => share.MemberId == Owner).Roles.Should().BeEmpty();
        document.Shares.Single(share => share.MemberId == Member).Roles.Select(held => held.RoleId).Should().Equal(Onlooker);
    }

    [Fact]
    public void Naming_an_owner_with_the_old_owner_leaving_takes_the_old_owner_off_the_list()
    {
        var document = Written();
        document.ShareWith(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        Forget(document);

        document.HandOver(Member, FormerOwner.Leaves, Now.AddDays(1));

        document.Shares.Should().ContainSingle().Which.MemberId.Should().Be(Member);
        Raised(document).Should().Equal(
            Bare(new DocumentRoleTaken(document.Id, Member, Onlooker)),
            Bare(new DocumentUnshared(document.Id, Owner)),
            Bare(new DocumentHandedOver(document.Id, Owner, Member)));
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void Nothing_is_assumed_for_the_old_owner()
    {
        // There is no role the old owner falls back to, since it held none: the command says what it becomes.
        typeof(MemberList<,,,>).GetMethod("NameOwner")!.GetParameters().Select(parameter => parameter.ParameterType.Name)
            .Should().Contain("FormerOwner`1");
        FluentActions.Invoking(() => Written().HandOver(Member, null!, Now)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Naming_an_owner_whose_membership_had_ended_says_that_it_began_anew_and_which_roles_went()
    {
        List<DocumentShare> shares = [];
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, Codes);
        list.Open(Now);
        list.Add(Member, Contributor, MemberPeriod.Between(Now, Now.AddDays(30)), Now, by: Owner);
        list.GiveRole(Member, Onlooker, MemberPeriod.Open(Now), Now, by: Owner);
        var later = Now.AddDays(90);

        var named = list.NameOwner(Member, FormerOwner.Leaves, later);

        // Not added: the member was on the list. But nothing of the membership that ended is left, and the answer says
        // so, so an aggregate that raises an event for every role a member loses can raise them.
        (named.Added, named.BeganAnew, named.PreviousRole, named.PreviousLeft).Should().Be((false, true, (NamedRole?)null, true));
        named.RolesDropped.Select(held => (held.RoleId, held.StartsAt, held.EndsAt))
            .Should().Equal((Contributor, Now, Now.AddDays(30)), (Onlooker, Now, null));
        (named.EndLifted, named.StartMoved).Should().Be((null, null), "the membership that ended is over: nothing of it was changed");

        var member = shares.Single(share => share.MemberId == Member);
        (member.StartsAt, member.EndsAt, member.Roles.Count).Should().Be((later, null, 0), "the membership does not read as unbroken since it first began");
    }

    [Fact]
    public void Naming_an_owner_whose_membership_is_yet_to_start_or_to_end_starts_it_now_for_good_and_says_so()
    {
        List<DocumentShare> shares = [];
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, Codes);
        list.Open(Now);
        var dee = list.Add(Member, Onlooker, MemberPeriod.Between(Now.AddDays(10), Now.AddDays(20)), Now, by: Owner);

        var named = list.NameOwner(Member, FormerOwner.StaysAs(Contributor), Now.AddDays(1));

        // The answer says both changes of the period: the start that was moved, and the end that went.
        (named.StartMoved, named.EndLifted, named.BeganAnew, named.Added).Should().Be((Now.AddDays(10), Now.AddDays(20), false, false));
        named.RolesDropped.Select(held => held.RoleId).Should().Equal(Onlooker);
        (named.PreviousRole, named.PreviousLeft).Should().Be((Contributor, false));
        (dee.StartsAt, dee.EndsAt).Should().Be((Now.AddDays(1), null), "an owner is a member from the moment it is named, for good");
    }

    [Fact]
    public void Naming_an_owner_who_is_no_member_adds_them()
    {
        var document = Written();
        Forget(document);

        document.HandOver(Member, FormerOwner.StaysAs(Onlooker), Now.AddDays(1));

        document.Shares.Select(share => (share.MemberId, Roles: share.Roles.Count)).Should().Equal((Owner, 1), (Member, 0));
        document.Shares.Single(share => share.MemberId == Member).AddedBy.Should().BeNull("nobody added the owner: they were named");
        Raised(document).Should().Equal(
            Bare(new DocumentShared(document.Id, Member)),
            Bare(new DocumentRoleGiven(document.Id, Owner, Onlooker)),
            Bare(new DocumentHandedOver(document.Id, Owner, Member)));

        // The old owner is a member like any other now, and can be removed.
        document.Unshare(Owner);
        document.Shares.Should().ContainSingle().Which.MemberId.Should().Be(Member);
    }

    [Fact]
    public void Naming_an_owner_answers_what_happened_for_the_aggregate_to_act_on()
    {
        List<DocumentShare> shares = [];
        var list = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, Owner, Codes);
        list.Open(Now);

        var named = list.NameOwner(Member, FormerOwner.StaysAs(Onlooker), Now.AddDays(1));

        (named.Previous, named.Owner, named.Added, named.PreviousRole, named.PreviousLeft).Should().Be((Owner, Member, true, Onlooker, false));
        (named.BeganAnew, named.EndLifted, named.StartMoved).Should().Be((false, null, null), "somebody who was no member had no membership to change");
        named.RolesDropped.Should().BeEmpty();

        // The list does not keep who the owner is: until the aggregate does, the list still protects the old one.
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => list.Remove(Owner));
        var kept = new MemberList<DocumentShare, DocumentShareId, UserId, NamedRole>(shares, named.Owner, Codes);
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
            () => document.TakeRole(Member, Contributor),
            () => document.Unshare(Member),
            () => document.HandOver(Member, FormerOwner.Leaves, Now),
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
        document.TakeRole(Member, Contributor);
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
        Refused.With(Codes, MembershipRefusals.RoleNotHeld, () => document.TakeRole(Member, Onlooker));
        Refused.With(Codes, MembershipRefusals.OwnerProtected, () => document.Unshare(Owner));
        Refused.With(Codes, MembershipRefusals.AlreadyOwner, () => document.HandOver(Owner, FormerOwner.Leaves, Now));

        document.DomainEvents.Should().BeEmpty();
        document.Shares.Select(share => (share.MemberId, Roles: share.Roles.Count)).Should().Equal((Owner, 0), (Member, 1));
    }

    // ---------------------------------------------------------------- the net under the methods

    [Fact]
    public void A_member_that_is_on_the_list_twice_is_caught_under_the_resources_own_code()
    {
        var document = Written();
        document.ShareWith(Member, MemberPeriod.Open(Now), Now, by: Owner);

        // Went round the member list: the row of another document's member, for the same user.
        var elsewhere = new Document(DocumentId.CreateSequential(), "Elsewhere", Member, Now);
        Break.ListOf<DocumentShare>(document, "_shares").Add(elsewhere.Shares.Single());

        var violation = document.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("documents.already-member");
        violation.EntityType.Should().Be<Document>();
        violation.Arguments.Should().Contain("Member", Member);
        violation.Message.Should().Be(MembershipRefusals.TemplateOf(MembershipRefusals.AlreadyMember), "the rule reports the text of the refusal, so one translation serves both");
    }

    [Theory]
    [InlineData("the membership ends")]
    [InlineData("a role is held")]
    [InlineData("the owner is no member")]
    public void An_owner_whose_place_runs_out_or_who_holds_a_role_is_caught_under_the_resources_own_code(string broken)
    {
        var document = Written();
        var owner = document.Shares.Single();
        switch (broken)
        {
            case "the membership ends":
                Break.Set(owner, nameof(DocumentShare.EndsAt), (DateTimeOffset?)Now.AddDays(30));
                break;
            case "a role is held":
                // Went round the member list: a role held on another document, put on the owner's place.
                var elsewhere = new Document(DocumentId.CreateSequential(), "Elsewhere", Member, Now);
                var held = elsewhere.ShareWith(Owner, Contributor, MemberPeriod.Open(Now), Now, by: Member).Roles.Single();
                Break.ListOf<MemberRole<UserId, NamedRole>>(owner, "_roles").Add(held);
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
        var elsewhere = new Document(DocumentId.CreateSequential(), "Elsewhere", Member, Now);
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

    /// <summary>A document written now, owned by <see cref="Owner"/>, who is on it for good, holding no role.</summary>
    private static Document Written() => new(DocumentId.CreateSequential(), "Minutes", Owner, Now);

    /// <summary>Forgets the events raised so far, so a test reads only what its own change raised.</summary>
    private static void Forget(Document document) => ((IHasDomainEvents)document).DequeueDomainEvents();

    /// <summary>The events the document raised and still holds, each without what tells one raising from another.</summary>
    private static IEnumerable<DomainEvent> Raised(Document document) => document.DomainEvents.Cast<DomainEvent>().Select(Bare);

    /// <summary>An event without its own id and moment, so two that say the same compare as equal.</summary>
    private static DomainEvent Bare(DomainEvent raised) => raised with { EventId = Guid.Empty, OccurredAt = default };
}
