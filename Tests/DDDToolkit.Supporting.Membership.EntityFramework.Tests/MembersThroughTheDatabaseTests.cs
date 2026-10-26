using DDDToolkit.Exceptions;
using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The member list's rules through the database: a resource is saved and read back with its members exactly
/// as the member list left them, every change of the members is one save of the resource under its one
/// version, and what went round the member list is refused when it is saved.
/// </summary>
public sealed class MembersThroughTheDatabaseTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_resource_is_read_back_with_its_members_and_the_roles_they_hold()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        foreach (var written in data.Documents)
        {
            var read = await filing.Services.ReadAsync(written.Id);

            read.OwnerId.Should().Be(written.OwnerId);
            MemberOverviews.From(read.Shares, read.OwnerId, data.Now).Should().BeEquivalentTo(
                MemberOverviews.From(written.Shares, written.OwnerId, data.Now),
                options => options.WithStrictOrdering(),
                "every member, period and role comes back as the member list left it");
            read.Shares.Select(share => (share.Id, share.MemberId, share.AddedBy)).Should().BeEquivalentTo(written.Shares.Select(share => (share.Id, share.MemberId, share.AddedBy)));
            read.Shares.SelectMany(share => share.Roles.Select(held => (share.MemberId, held.RoleId, held.GivenBy)))
                .Should().BeEquivalentTo(written.Shares.SelectMany(share => share.Roles.Select(held => (share.MemberId, held.RoleId, held.GivenBy))));
            read.GetInvariantViolations().Should().BeEmpty("what was read is a document its own rules and the package's hold for");
        }
    }

    [Fact]
    public async Task A_folders_staff_are_read_back_from_the_hosts_own_tables_with_what_the_host_added()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        await filing.Services.AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var cabinet = await context.Folders.SingleAsync(folder => folder.Id == data.Cabinet, Cancellation);
            cabinet.Staff.Single(member => member.MemberId == data.Clerk).Annotate("Covers for the keeper");
            await context.SaveChangesAsync(Cancellation);
        });

        var read = await filing.Services.ReadAsync(data.Cabinet);
        var written = data.Folders.Single(folder => folder.Id == data.Cabinet);

        read.Keeper.Should().Be(data.Keeper);
        MemberOverviews.From(read.Staff, read.Keeper, data.Now).Should().BeEquivalentTo(MemberOverviews.From(written.Staff, written.Keeper, data.Now), options => options.WithStrictOrdering());
        read.Staff.Single(member => member.MemberId == data.Clerk).Note.Should().Be("Covers for the keeper");
        read.Staff.Single(member => member.MemberId == data.Temp).Roles.Should().BeEmpty("a role that was taken is gone from the database");

        // In the tables the host named, with the member under the host's own word for it.
        using var scope = filing.Services.Provider.CreateScope();
        var connection = (SqliteConnection)scope.ServiceProvider.GetRequiredService<FilingContext>().Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT group_concat("{FilingContext.StaffColumn}", ',') FROM (SELECT "{FilingContext.StaffColumn}" FROM "{FilingContext.FolderStaffTable}" WHERE "FolderId" = 7 ORDER BY 1))
                   || ' / ' || (SELECT count(*) FROM "{FilingContext.FolderStaffRolesTable}" WHERE "FolderId" = 7)
            """;
        command.ExecuteScalar().Should().Be("C-014,K-001,T-030,V-020 / 3");
    }

    [Fact]
    public async Task Every_change_of_the_members_is_one_save_of_the_resource_under_its_one_version()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var now = data.Now;
        var version = (await filing.Services.ReadAsync(data.Minutes)).Version;

        async Task<Document> SavedAsync(Action<Document> change)
        {
            await filing.Services.ChangeAsync(data.Minutes, change);
            var read = await filing.Services.ReadAsync(data.Minutes);
            read.Version.Should().Be(++version, "the members have no version of their own: a change of theirs is a change of the document");
            read.GetInvariantViolations().Should().BeEmpty();
            return read;
        }

        // Add, with a role.
        var added = await SavedAsync(document => document.ShareWith(data.Hal, DocumentMembership.Onlooker, MemberPeriod.Open(now), now, by: data.Ada));
        added.Shares.Single(share => share.MemberId == data.Hal).HoldsAt(DocumentMembership.Onlooker, now).Should().BeTrue();

        // Give a second role, next to the first.
        var given = await SavedAsync(document => document.GiveRole(data.Hal, DocumentMembership.Contributor, MemberPeriod.Between(now, now.AddDays(3)), now, by: data.Ada));
        given.Shares.Single(share => share.MemberId == data.Hal).Roles.Select(held => (held.RoleId, held.EndsAt, held.GivenBy))
            .Should().BeEquivalentTo([(DocumentMembership.Onlooker, (DateTimeOffset?)null, (UserId?)data.Ada), (DocumentMembership.Contributor, now.AddDays(3), data.Ada)]);

        // Take one, and the member stays with the other.
        var taken = await SavedAsync(document => document.TakeRole(data.Hal, DocumentMembership.Onlooker, DocumentMembership.Owner));
        taken.Shares.Single(share => share.MemberId == data.Hal).Roles.Should().ContainSingle().Which.RoleId.Should().Be(DocumentMembership.Contributor);

        // Remove, with every role held.
        var removed = await SavedAsync(document => document.Unshare(data.Hal));
        removed.Shares.Should().NotContain(share => share.MemberId == data.Hal);
        removed.Shares.Should().HaveCount(data.Documents[0].Shares.Count, "nobody else was touched");
    }

    [Fact]
    public async Task An_ended_membership_begins_again_and_a_role_that_ran_out_is_given_again_each_in_one_save()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var now = data.Now;
        var before = await filing.Services.ReadAsync(data.Minutes);

        // Eve's membership ended yesterday: she comes back with a new one, without the role of the old.
        await filing.Services.ChangeAsync(data.Minutes, document => document.ShareWith(data.Eve, MemberPeriod.Open(now), now, by: data.Ada));
        var again = (await filing.Services.ReadAsync(data.Minutes)).Shares.Where(share => share.MemberId == data.Eve).Should().ContainSingle().Subject;
        (again.StartsAt, again.EndsAt).Should().Be((now, null));
        again.Roles.Should().BeEmpty("the roles of the membership that ended ran out with it");
        again.Id.Should().NotBe(before.Shares.Single(share => share.MemberId == data.Eve).Id, "it is a new membership, and a new row");

        // Fay's role as a contributor ended yesterday: given again, it is the one hold of that role, from now.
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Fay, DocumentMembership.Contributor, MemberPeriod.Open(now), now, by: data.Ada));
        var fay = (await filing.Services.ReadAsync(data.Minutes)).Shares.Single(share => share.MemberId == data.Fay);
        fay.Roles.Where(held => held.RoleId == DocumentMembership.Contributor).Should().ContainSingle().Which.Should().Match<MemberRole<UserId, NamedRole>>(held => held.StartsAt == now && held.EndsAt == null);
        fay.HoldsAt(DocumentMembership.Contributor, now).Should().BeTrue();
        fay.Roles.Should().HaveCount(2, "the role that starts tomorrow is still hers");
    }

    [Fact]
    public async Task Naming_another_owner_is_one_save_and_what_the_database_holds_keeps_the_owners_place()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var now = data.Now;
        var version = (await filing.Services.ReadAsync(data.Minutes)).Version;

        // To Ben, a member already: he keeps his own role, gets the owner's, and Ada loses that one alone.
        await filing.Services.ChangeAsync(data.Minutes, document => document.HandOver(data.Ben, DocumentMembership.Owner, now));
        var handed = await filing.Services.ReadAsync(data.Minutes);

        handed.OwnerId.Should().Be(data.Ben);
        handed.Version.Should().Be(version + 1);
        handed.Shares.Single(share => share.MemberId == data.Ben).Roles.Select(held => held.RoleId).Should().BeEquivalentTo([DocumentMembership.Contributor, DocumentMembership.Owner]);
        handed.Shares.Single(share => share.MemberId == data.Ada).Roles.Should().BeEmpty("the owner before stays a member, without the owner's role");
        handed.GetInvariantViolations().Should().BeEmpty();

        // To Hal, who was no member: added, for good, as the owner.
        await filing.Services.ChangeAsync(data.Minutes, document => document.HandOver(data.Hal, DocumentMembership.Owner, now));
        var again = await filing.Services.ReadAsync(data.Minutes);

        again.OwnerId.Should().Be(data.Hal);
        again.Shares.Single(share => share.MemberId == data.Hal).Should().Match<DocumentShare>(share => share.StartsAt == now && share.EndsAt == null && share.HoldsAt(DocumentMembership.Owner, now));
        again.GetInvariantViolations().Should().BeEmpty();

        // And the access questions follow the row: every key of the document is Hal's now, and what Ada held as
        // its owner she holds no more.
        (await filing.DocumentsAsync(TestCallers.User(data.Hal), access => access.HoldAsync(data.Minutes, DocumentKeys.Share, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await filing.DocumentsAsync(TestCallers.User(data.Ada), access => access.HoldAsync(data.Minutes, DocumentKeys.Share, Cancellation)))!.Via.Should().BeNull();
    }

    [Fact]
    public async Task What_the_member_list_refuses_reaches_no_row()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var before = await filing.Services.ReadAsync(data.Minutes);

        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.OwnerProtected, () => filing.Services.ChangeAsync(data.Minutes, document => document.Unshare(data.Ada)));
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.AlreadyMember, () => filing.Services.ChangeAsync(data.Minutes, document => document.ShareWith(data.Ben, MemberPeriod.Open(data.Now), data.Now, by: data.Ada)));
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.RoleHeld, () => filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Ben, DocumentMembership.Contributor, MemberPeriod.Open(data.Now), data.Now, by: data.Ada)));

        var after = await filing.Services.ReadAsync(data.Minutes);
        after.Version.Should().Be(before.Version);
        MemberOverviews.From(after.Shares, after.OwnerId, data.Now).Should().BeEquivalentTo(MemberOverviews.From(before.Shares, before.OwnerId, data.Now), options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task What_went_round_the_member_list_is_refused_when_the_resource_is_saved()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // The owner's membership given an end, behind the member list's back: the document's own rule, which asks
        // the member list's check, refuses the save under the document's code.
        var saved = await FluentActions.Awaiting(() => filing.Services.ChangeAsync(data.Minutes, document =>
                typeof(MemberEntity<DocumentShareId, UserId, NamedRole>).GetProperty(nameof(DocumentShare.EndsAt))!
                    .GetSetMethod(nonPublic: true)!.Invoke(document.Shares.Single(share => share.MemberId == data.Ada), [data.Now.AddDays(1)])))
            .Should().ThrowAsync<InvariantViolationException>();

        saved.Which.InvariantViolations.Select(violation => violation.Code).Should().Equal(DocumentRefusals.Membership[MembershipRefusals.OwnerProtected]);
        (await filing.Services.ReadAsync(data.Minutes)).Shares.Single(share => share.MemberId == data.Ada).EndsAt.Should().BeNull("nothing of a refused save is written");
    }
}
