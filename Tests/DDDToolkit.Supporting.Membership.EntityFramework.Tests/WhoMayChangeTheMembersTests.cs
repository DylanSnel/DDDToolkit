using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// Who may add a member or give a role is the host's to decide, and the host of these tests decides it in two
/// ways. A document is shared by whoever holds one key on it, and that is the whole rule: one line on the
/// request. Staff are put on a folder by whoever holds a key on it too, and then a rule the host wrote itself
/// applies, which reads until when the caller holds that key: nobody is put on a folder for longer than that.
/// <para>
/// The package decides neither. It answers who holds which key and until when, keeps the member list's own
/// rules, and asks whether a member and a role exist. Each request is sent the way a host's dispatcher does:
/// the checks of the module, then its handler.
/// </para>
/// </summary>
public sealed class WhoMayChangeTheMembersTests
{
    // ---------------------------------------------------------------- documents: a key on the resource, and nothing else

    [Fact]
    public async Task A_document_is_shared_by_whoever_holds_the_key_that_shares_on_it()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var until = data.Now.AddDays(2);

        // Ada owns the minutes, and an owner holds every key of a document.
        await filing.Services.SendAsync(TestCallers.User(data.Ada), new ShareDocument(data.Minutes, data.Hal, DocumentMembership.Onlooker, until));

        var shared = (await filing.Services.ReadAsync(data.Minutes)).Shares.Single(share => share.MemberId == data.Hal);
        (shared.StartsAt, shared.EndsAt, shared.AddedBy).Should().Be((data.Now, until, data.Ada));
        shared.Roles.Should().ContainSingle().Which.Should().Match<MemberRole<UserId, NamedRole>>(held =>
            held.RoleId == DocumentMembership.Onlooker && held.EndsAt == until && held.GivenBy == data.Ada);

        // Ian is no owner. He holds the owner's role, which gives the key, and that is all the request asks.
        var newcomer = UserId.CreateSequential();
        await filing.Services.SendAsync(TestCallers.User(data.Ian), new ShareDocument(data.Minutes, newcomer));
        (await filing.Services.ReadAsync(data.Minutes)).Shares.Single(share => share.MemberId == newcomer).AddedBy.Should().Be(data.Ian);
    }

    [Fact]
    public async Task Without_the_key_a_document_is_not_shared_and_nothing_is_written()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var before = (await filing.Services.ReadAsync(data.Minutes)).Version;

        // Ben contributes: he sees the minutes and does not hold the key. To Hal the minutes are not there.
        (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted,
            () => filing.Services.SendAsync(TestCallers.User(data.Ben), new ShareDocument(data.Minutes, data.Hal, DocumentMembership.Onlooker))))
            .Arguments.Should().Contain("Key", DocumentKeys.Share);
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound,
            () => filing.Services.SendAsync(TestCallers.User(data.Hal), new ShareDocument(data.Minutes, data.Hal, DocumentMembership.Onlooker)));

        // What makes a share well formed is still asked, of whoever may share: a role there is for a document.
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.RoleNotForMembers,
            () => filing.Services.SendAsync(TestCallers.User(data.Ada), new ShareDocument(data.Minutes, data.Hal, FolderMembership.Clerk)));

        var after = await filing.Services.ReadAsync(data.Minutes);
        after.Version.Should().Be(before);
        after.Shares.Should().NotContain(share => share.MemberId == data.Hal);
    }

    [Fact]
    public async Task A_document_has_no_rule_about_how_long_so_a_hold_that_ends_shares_for_good()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Dee holds the key that shares for two more hours, through the owner's role.
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Dee, DocumentMembership.Owner, MemberPeriod.Between(data.Now, data.Now.AddHours(2)), data.Now, by: data.Ada));
        (await filing.DocumentsAsync(TestCallers.User(data.Dee), access => access.HoldAsync(data.Minutes, DocumentKeys.Share, TestContext.Current.CancellationToken)))!.Until.Should().Be(data.Now.AddHours(2));

        // She shares with no end, in the very role she holds for two hours. The folders' rule is the folders'.
        await filing.Services.SendAsync(TestCallers.User(data.Dee), new ShareDocument(data.Minutes, data.Hal, DocumentMembership.Owner));

        var shared = (await filing.Services.ReadAsync(data.Minutes)).Shares.Single(share => share.MemberId == data.Hal);
        (shared.EndsAt, shared.AddedBy).Should().Be(((DateTimeOffset?)null, (UserId?)data.Dee));
        shared.Roles.Single().EndsAt.Should().BeNull();
    }

    [Fact]
    public async Task A_share_is_written_to_the_document_that_was_checked_at_the_version_it_was_checked_at()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var read = (await filing.Services.ReadAsync(data.Minutes)).Version;

        await filing.Services.SendAsync(TestCallers.User(data.Ada), new ShareDocument(data.Minutes, data.Hal, ExpectedVersion: read));
        (await filing.Services.ReadAsync(data.Minutes)).Version.Should().Be(read + 1);

        // The same version again: somebody changed the members since, and that somebody was this caller.
        var lost = await FluentActions.Awaiting(() => filing.Services.SendAsync(TestCallers.User(data.Ada), new ShareDocument(data.Minutes, UserId.CreateSequential(), ExpectedVersion: read)))
            .Should().ThrowAsync<ConcurrencyConflictException>();
        (lost.Which.AggregateType, lost.Which.AggregateId).Should().Be((typeof(Document), data.Minutes));
    }

    // ---------------------------------------------------------------- folders: a key, and the host's own rule over the hold's end

    /// <summary>The visitor of the cabinet, made a keeper's deputy for three days: the keeper's role, which gives the key that decides a folder's staff.</summary>
    private static async Task<DateTimeOffset> DeputyAsync(SqliteFiling filing)
    {
        var until = filing.Scenario.Now.AddDays(3);
        await filing.Services.ChangeAsync(filing.Scenario.Cabinet, folder => folder.GiveRole(filing.Scenario.Visitor, FolderMembership.Keeper, MemberPeriod.Between(filing.Scenario.Now, until), filing.Scenario.Now, by: filing.Scenario.Keeper));
        return until;
    }

    [Fact]
    public async Task Staff_are_put_on_a_folder_for_no_longer_than_the_caller_holds_the_key_that_lets_them()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var mine = await DeputyAsync(filing);
        var deputy = TestCallers.Staff(data.Visitor);
        var before = (await filing.Services.ReadAsync(data.Cabinet)).Version;

        // For longer than the deputy's own three days, and with no end at all: refused by the host's own rule,
        // under the host's own code, which names until when the caller could have given.
        foreach (var until in new DateTimeOffset?[] { mine.AddTicks(1), mine.AddDays(30), null })
        {
            var refused = (await FluentActions.Awaiting(() => filing.Services.SendAsync(deputy, new AdmitStaff(data.Cabinet, new StaffCode("N-100"), FolderMembership.Clerk, until)))
                .Should().ThrowAsync<RefusalException>()).Which;

            (refused.Code, refused.Kind).Should().Be((FolderRefusals.LongerThanHeld, RefusalKind.NotPermitted));
            refused.Arguments.Should().Contain("Until", mine);
        }

        var untouched = await filing.Services.ReadAsync(data.Cabinet);
        untouched.Version.Should().Be(before, "a refused admission writes nothing");
        untouched.Staff.Should().NotContain(member => member.MemberId == new StaffCode("N-100"));

        // Within it, to the very moment it ends: admitted, by the deputy.
        await filing.Services.SendAsync(deputy, new AdmitStaff(data.Cabinet, new StaffCode("N-100"), FolderMembership.Clerk, mine.AddDays(-1)));
        await filing.Services.SendAsync(deputy, new AdmitStaff(data.Cabinet, new StaffCode("N-200"), FolderMembership.Visitor, mine));

        var cabinet = await filing.Services.ReadAsync(data.Cabinet);
        var first = cabinet.Staff.Single(member => member.MemberId == new StaffCode("N-100"));
        (first.EndsAt, first.AddedBy).Should().Be(((DateTimeOffset?)mine.AddDays(-1), (StaffCode?)data.Visitor));
        cabinet.Staff.Single(member => member.MemberId == new StaffCode("N-200")).EndsAt.Should().Be(mine);
    }

    [Fact]
    public async Task The_keeper_and_the_applications_own_work_hold_the_key_with_no_end_and_admit_for_as_long_as_they_say()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        await filing.Services.SendAsync(TestCallers.Staff(data.Keeper), new AdmitStaff(data.Cabinet, new StaffCode("N-300"), FolderMembership.Clerk));
        await filing.Services.SendAsync(Caller.System, new AdmitStaff(data.Cabinet, new StaffCode("N-400"), FolderMembership.Visitor, data.Now.AddYears(5)));

        var cabinet = await filing.Services.ReadAsync(data.Cabinet);
        var byKeeper = cabinet.Staff.Single(member => member.MemberId == new StaffCode("N-300"));
        (byKeeper.EndsAt, byKeeper.AddedBy).Should().Be(((DateTimeOffset?)null, (StaffCode?)data.Keeper));
        var bySystem = cabinet.Staff.Single(member => member.MemberId == new StaffCode("N-400"));
        (bySystem.EndsAt, bySystem.AddedBy).Should().Be(((DateTimeOffset?)data.Now.AddYears(5), (StaffCode?)null));
    }

    [Fact]
    public async Task The_key_comes_first_and_the_hosts_rule_is_asked_only_of_who_holds_it()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var mine = await DeputyAsync(filing);

        // The clerk is on the cabinet without the key that decides its staff: refused by the check, under the package's
        // rule and the folder's code, whatever period is asked for. Somebody who is on no folder does not see it.
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted,
            () => filing.Services.SendAsync(TestCallers.Staff(data.Clerk), new AdmitStaff(data.Cabinet, new StaffCode("N-500"), FolderMembership.Visitor, data.Now.AddHours(1)))))
            .Arguments.Should().Contain("Key", FolderKeys.Staff);
        await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotFound,
            () => filing.Services.SendAsync(TestCallers.Staff(data.Nobody), new AdmitStaff(data.Cabinet, new StaffCode("N-500"), FolderMembership.Visitor, data.Now.AddHours(1))));

        // Once the deputy's three days are over the key is no longer theirs, and neither is the question of how long.
        filing.Clock.Advance(TimeSpan.FromDays(3));
        await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted,
            () => filing.Services.SendAsync(TestCallers.Staff(data.Visitor), new AdmitStaff(data.Cabinet, new StaffCode("N-500"), FolderMembership.Visitor, mine)));

        (await filing.Services.ReadAsync(data.Cabinet)).Staff.Should().NotContain(member => member.MemberId == new StaffCode("N-500"));
    }

    [Fact]
    public async Task What_makes_an_admission_well_formed_is_asked_after_the_hosts_rule_and_the_member_lists_own_rules_last()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var keeper = TestCallers.Staff(data.Keeper);

        // A role that is none of a folder's, and somebody who is on the folder already.
        await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.RoleNotForMembers,
            () => filing.Services.SendAsync(keeper, new AdmitStaff(data.Cabinet, new StaffCode("N-600"), DocumentMembership.Contributor)));
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.AlreadyMember,
            () => filing.Services.SendAsync(keeper, new AdmitStaff(data.Cabinet, data.Clerk, FolderMembership.Visitor))))
            .Code.Should().Be("folders.already-on-folder");

        // In that order. Asked for longer than the deputy holds the key, in a role that is none of a folder's:
        // the host's rule answers. Within the hold, in that role, for somebody already on the folder: the role does.
        var mine = await DeputyAsync(filing);
        (await FluentActions.Awaiting(() => filing.Services.SendAsync(TestCallers.Staff(data.Visitor), new AdmitStaff(data.Cabinet, data.Clerk, DocumentMembership.Contributor, mine.AddDays(1))))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(FolderRefusals.LongerThanHeld);
        await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.RoleNotForMembers,
            () => filing.Services.SendAsync(TestCallers.Staff(data.Visitor), new AdmitStaff(data.Cabinet, data.Clerk, DocumentMembership.Contributor, mine)));

        // The folder's own guard still stands in front of its member list.
        await filing.Services.ChangeAsync(data.Cabinet, folder => folder.Lock());
        (await FluentActions.Awaiting(() => filing.Services.SendAsync(keeper, new AdmitStaff(data.Cabinet, new StaffCode("N-600"), FolderMembership.Clerk)))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(FolderRefusals.Locked);
    }
}
