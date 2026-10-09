using DDDToolkit.Localization;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The members of a resource as a caller is shown them: in one order, the same every time, and with what
/// counts now said for each member and each role.
/// </summary>
public sealed class MemberOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId First = new(new Guid("00000000-0000-0000-0000-000000000001"));

    private static readonly UserId Second = new(new Guid("00000000-0000-0000-0000-000000000002"));

    private static readonly UserId Third = new(new Guid("00000000-0000-0000-0000-000000000003"));

    private static readonly UserId Fourth = new(new Guid("00000000-0000-0000-0000-000000000004"));

    [Fact]
    public void The_owner_comes_first_then_members_by_when_they_start_then_by_member()
    {
        // The owner is the last to have joined and has the highest id: it comes first all the same.
        var document = new Document(DocumentId.CreateSequential(), "Minutes", Fourth, DocumentMembership.Owner, Now.AddDays(5));
        document.ShareWith(Third, MemberPeriod.Open(Now.AddDays(1)), Now, by: Fourth);
        document.ShareWith(Second, MemberPeriod.Open(Now), Now, by: Fourth);
        document.ShareWith(First, MemberPeriod.Open(Now.AddDays(1)), Now, by: Fourth);

        var overview = MemberOverviews.From(document.Shares, document.OwnerId, Now.AddDays(10));

        overview.Select(member => member.Member).Should().Equal(Fourth, Second, First, Third);
        overview.Select(member => member.IsOwner).Should().Equal(true, false, false, false);

        // The same members in any order they were read in are listed in the same order.
        MemberOverviews.From(document.Shares.Reverse(), document.OwnerId, Now.AddDays(10)).Select(member => member.Member)
            .Should().Equal(Fourth, Second, First, Third);
    }

    [Fact]
    public void A_members_roles_are_listed_by_when_each_starts_then_by_role()
    {
        var document = new Document(DocumentId.CreateSequential(), "Minutes", First, DocumentMembership.Owner, Now);
        document.GiveRole(First, DocumentMembership.Onlooker, MemberPeriod.Open(Now), Now, by: First);
        document.GiveRole(First, DocumentMembership.Contributor, MemberPeriod.Between(Now.AddDays(-3), Now.AddDays(3)), Now, by: First);

        var owner = MemberOverviews.From(document.Shares, document.OwnerId, Now).Should().ContainSingle().Which;

        // The contributor's role started first; the owner's and the onlooker's started together, and are told apart by role.
        owner.Roles.Select(held => held.Role.Value).Should().Equal("contributor", "onlooker", "owner");
        owner.Roles.Select(held => (held.StartsAt, held.EndsAt)).Should().Equal(
            (Now.AddDays(-3), Now.AddDays(3)),
            (Now, null),
            (Now, null));
    }

    [Fact]
    public void A_role_is_said_to_count_now_only_inside_its_membership()
    {
        var document = new Document(DocumentId.CreateSequential(), "Minutes", First, DocumentMembership.Owner, Now);
        document.ShareWith(Second, MemberPeriod.Between(Now, Now.AddDays(30)), Now, by: First);
        document.GiveRole(Second, DocumentMembership.Contributor, MemberPeriod.Between(Now, Now.AddDays(7)), Now, by: First);
        document.GiveRole(Second, DocumentMembership.Onlooker, MemberPeriod.Open(Now), Now, by: First);
        document.ShareWith(Third, DocumentMembership.Onlooker, MemberPeriod.Open(Now.AddDays(20)), Now, by: First);

        MemberOverview<UserId, NamedRole> Of(UserId member, DateTimeOffset moment)
            => MemberOverviews.From(document.Shares, document.OwnerId, moment).Single(shown => shown.Member == member);

        // On day 10 the membership counts, the contributor's role has run out and the onlooker's goes on.
        var midway = Of(Second, Now.AddDays(10));
        (midway.StartsAt, midway.EndsAt, midway.AppliesNow).Should().Be((Now, Now.AddDays(30), true));
        midway.Roles.Select(held => (held.Role, held.AppliesNow)).Should().Equal((DocumentMembership.Contributor, false), (DocumentMembership.Onlooker, true));

        // On day 30 the membership is over, and with it the onlooker's role, which had no end of its own.
        var after = Of(Second, Now.AddDays(30));
        after.AppliesNow.Should().BeFalse();
        after.Roles.Should().OnlyContain(held => !held.AppliesNow);
        after.Roles.Single(held => held.Role == DocumentMembership.Onlooker).EndsAt.Should().BeNull("the overview shows the role's own dates, and says separately what counts");

        // A membership that has not started counts for nothing yet, and neither does a role in it.
        var early = Of(Third, Now.AddDays(10));
        early.AppliesNow.Should().BeFalse();
        early.Roles.Should().ContainSingle().Which.AppliesNow.Should().BeFalse();
        Of(Third, Now.AddDays(20)).Roles.Single().AppliesNow.Should().BeTrue();

        // The owner's place and role do not run out.
        var owner = Of(First, Now.AddYears(50));
        (owner.IsOwner, owner.AppliesNow, owner.Roles.Single().AppliesNow).Should().Be((true, true, true));
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("da-DK")]
    [InlineData("nl-NL")]
    public void Members_and_roles_known_by_a_text_are_listed_in_one_order_whatever_the_readers_language(string language)
    {
        // Everything starts at the same moment, so only the member and the role tell the rows apart. A language
        // puts "a" before "B", and Danish puts "aa" after "z"; the characters themselves say otherwise both times.
        var keeper = new StaffCode("K-001");
        var folder = new Folder(new FolderId(7), keeper, FolderMembership.Keeper, Now);
        foreach (var staff in new[] { "z-1", "aa-1", "a-2", "B-1" })
        {
            folder.Admit(new StaffCode(staff), FolderMembership.Visitor, MemberPeriod.Open(Now), Now, by: keeper);
        }

        foreach (var role in new[] { "aa", "z", "alpha", "Zulu" })
        {
            folder.GiveRole(keeper, new NamedRole(role), MemberPeriod.Open(Now), Now, by: keeper);
        }

        IReadOnlyList<MemberOverview<StaffCode, NamedRole>> overview;
        using (CultureScope.Use(language))
        {
            overview = MemberOverviews.From(folder.Staff, folder.Keeper, Now);
        }

        overview.Select(member => member.Member.Value).Should().Equal(["K-001", "B-1", "a-2", "aa-1", "z-1"], "the owner first, then the members by the characters of their codes");
        overview[0].Roles.Select(held => held.Role.Value).Should().Equal("Zulu", "aa", "alpha", "keeper", "z");
    }

    [Fact]
    public void The_members_of_another_kind_of_resource_are_shown_the_same_way()
    {
        var keeper = new StaffCode("K-001");
        var folder = new Folder(new FolderId(7), keeper, FolderMembership.Keeper, Now);
        folder.Admit(new StaffCode("C-014"), FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: keeper);
        folder.Admit(new StaffCode("A-200"), FolderMembership.Visitor, MemberPeriod.Open(Now), Now, by: keeper);

        var overview = MemberOverviews.From(folder.Staff, folder.Keeper, Now);

        overview.Select(member => member.Member.Value).Should().Equal("K-001", "A-200", "C-014");
        overview[0].Roles.Single().Role.Should().Be(FolderMembership.Keeper);
        MemberOverviews.From(Array.Empty<FolderMember>(), keeper, Now).Should().BeEmpty();
        FluentActions.Invoking(() => MemberOverviews.From<FolderMemberId, StaffCode, NamedRole>(null!, keeper, Now)).Should().Throw<ArgumentNullException>();
    }
}
