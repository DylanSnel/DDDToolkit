using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// Two kinds of resource with members in one project, one context and one container: documents shared with
/// users, and folders with staff known by a claim. Each has its own member class, tables, rules, codes and
/// services, and neither touches the other.
/// </summary>
public sealed class TwoResourcesInOneContextTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_resource_is_asked_about_under_its_own_rules()
    {
        using var filing = await SqliteFiling.SeededAsync();
        using var scope = filing.Services.Provider.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<IMemberQuestions<DocumentId>>().Rules.Should().BeSameAs(DocumentMembership.Rules);
        provider.GetRequiredService<IMemberQuestions<FolderId>>().Rules.Should().BeSameAs(FolderMembership.Rules);

        provider.GetServices<MembershipRegistration>().Should().BeEquivalentTo(
        [
            new MembershipRegistration(typeof(FilingContext), typeof(Document), typeof(DocumentId), typeof(DocumentShare), DocumentMembership.Rules),
            new MembershipRegistration(typeof(FilingContext), typeof(Folder), typeof(FolderId), typeof(FolderMember), FolderMembership.Rules),
        ]);

        // The roles there are for a member are each resource's own.
        var ofDocuments = provider.GetRequiredService<IMemberRoles<DocumentId, NamedRole>>();
        var ofFolders = provider.GetRequiredService<IMemberRoles<FolderId, NamedRole>>();
        (await ofDocuments.ExistsAsync(DocumentMembership.Contributor, Cancellation)).Should().BeTrue();
        (await ofDocuments.ExistsAsync(FolderMembership.Clerk, Cancellation)).Should().BeFalse();
        (await ofFolders.ExistsAsync(FolderMembership.Clerk, Cancellation)).Should().BeTrue();
        (await ofFolders.ExistsAsync(DocumentMembership.Contributor, Cancellation)).Should().BeFalse();
        (await ofDocuments.FindOwnerRoleAsync(Cancellation)).Should().Be(DocumentMembership.Owner);
        (await ofFolders.FindOwnerRoleAsync(Cancellation)).Should().Be(FolderMembership.Keeper);

        // And so is what a handler asks before it changes the members, refused under the resource's own code.
        var documents = provider.GetRequiredService<MemberAdmission<DocumentId, UserId, NamedRole>>();
        var folders = provider.GetRequiredService<MemberAdmission<FolderId, StaffCode, NamedRole>>();
        (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.RoleNotForMembers, () => documents.RequireRoleAsync(FolderMembership.Clerk, Cancellation).AsTask()))
            .Code.Should().Be("documents.role-not-for-members");
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.RoleNotForMembers, () => folders.RequireRoleAsync(DocumentMembership.Contributor, Cancellation).AsTask()))
            .Code.Should().Be("folders.role-not-for-members");
        await folders.RequireRoleAsync(FolderMembership.Clerk, Cancellation);
        await folders.RequireMemberAsync(new StaffCode("N-777"), Cancellation);
    }

    [Fact]
    public async Task A_member_of_one_reaches_nothing_of_the_other()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Somebody on a folder whose staff code is, of all things, Ada's user id as text.
        var confusing = new StaffCode(data.Ada.Value.ToString());
        await filing.Services.AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var cabinet = await context.Folders.SingleAsync(folder => folder.Id == data.Cabinet, Cancellation);
            cabinet.Admit(confusing, FolderMembership.Clerk, MemberPeriod.Open(data.Now.AddDays(-1)), data.Now, by: data.Keeper);
            await context.SaveChangesAsync(Cancellation);
        });

        // Ada owns a document and is on no folder: a folder asks who she is by another fact about her.
        var ada = TestCallers.User(data.Ada);
        (await filing.FoldersAsync(ada, access => access.HoldAsync(data.Cabinet, FolderKeys.Read, Cancellation))).Should().BeNull();
        (await filing.FoldersAsync(ada, access => access.KeysOnAsync([data.Cabinet, data.Annex], FilingScenario.FolderKeysAsked, Cancellation))).Should().BeEmpty();
        await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotFound, () => filing.FoldersAsync(ada, access => access.RequireAsync(data.Cabinet, FolderKeys.Read, Cancellation)));

        // The keeper keeps a folder and is a member of no document.
        var keeper = TestCallers.Staff(data.Keeper);
        (await filing.FoldersAsync(keeper, access => access.HoldAsync(data.Cabinet, FolderKeys.Staff, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        foreach (var document in data.Documents)
        {
            (await filing.DocumentsAsync(keeper, access => access.HoldAsync(document.Id, DocumentKeys.View, Cancellation))).Should().BeNull();
        }

        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => filing.DocumentsAsync(keeper, access => access.RequireAsync(data.Minutes, DocumentKeys.View, Cancellation)));

        // A key of one resource is no key of the other, whoever asks.
        (await filing.DocumentsAsync(ada, access => access.HoldAsync(data.Minutes, FolderKeys.File, Cancellation)))!.Via.Should().BeNull();
        (await filing.FoldersAsync(keeper, access => access.HoldAsync(data.Cabinet, DocumentKeys.Edit, Cancellation)))!.Via.Should().BeNull();
    }

    [Fact]
    public async Task Each_resource_refuses_under_its_own_codes()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        var document = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.RequireAsync(data.Minutes, DocumentKeys.Edit, Cancellation)));
        var folder = await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted, () => filing.FoldersAsync(TestCallers.Staff(data.Visitor), access => access.RequireAsync(data.Cabinet, FolderKeys.File, Cancellation)));

        (document.Code, folder.Code).Should().Be(("documents.not-permitted", "folders.not-permitted"));
        document.Arguments.Should().Contain("Key", DocumentKeys.Edit);
        folder.Arguments.Should().Contain("Key", FolderKeys.File);
    }

    [Fact]
    public async Task The_checks_of_both_sit_in_one_set_and_each_decides_its_own_resource()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // A request about a document, by its owner: passed, in one statement, and what was read is kept for its handler.
        var version = (await filing.Services.ReadAsync(data.Minutes)).Version;
        filing.Commands.Reset();
        await filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
        {
            var share = new ShareDocument(data.Minutes, data.Hal, ExpectedVersion: version);
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(share, Cancellation);

            provider.GetRequiredService<Checked<MemberHold<DocumentId>>>().TakeFor(share).Should().Be(new MemberHold<DocumentId>(data.Minutes, MemberVia.Members, version, Until: null));
        });
        filing.Commands.Count.Should().Be(1);

        // A request about a folder, by its clerk, in the same set: decided by the folder's check.
        await filing.Services.AsAsync(TestCallers.Staff(data.Clerk), async provider =>
        {
            var file = new FileInFolder(data.Cabinet);
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(file, Cancellation);

            provider.GetRequiredService<Checked<MemberHold<FolderId>>>().TakeFor(file).Via.Should().Be(MemberVia.Members);
        });

        // Refused under the code of the resource the request is about.
        Task RequireAsync(Caller caller, IFilingRequest request)
            => filing.Services.AsAsync(caller, provider => provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(request, Cancellation).AsTask());

        (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => RequireAsync(TestCallers.User(data.Ben), new ShareDocument(data.Minutes, data.Hal)))).Code.Should().Be("documents.not-permitted");
        (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => RequireAsync(TestCallers.Staff(data.Clerk), new ShareDocument(data.Minutes, data.Hal)))).Code.Should().Be("documents.not-found");
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted, () => RequireAsync(TestCallers.Staff(data.Visitor), new FileInFolder(data.Cabinet)))).Code.Should().Be("folders.not-permitted");
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotFound, () => RequireAsync(TestCallers.User(data.Ada), new FileInFolder(data.Cabinet)))).Code.Should().Be("folders.not-found");

        // A list refuses nobody for want of the key: its statement shows what the caller reaches, which may be
        // nothing. A caller who did not sign in reaches nothing under any rules, and is refused before its
        // statement, under the codes of the resource the list is of.
        filing.Commands.Reset();
        await RequireAsync(TestCallers.User(data.Hal), new ListDocuments());
        await RequireAsync(TestCallers.Staff(data.Nobody), new ListFolders());
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted, () => RequireAsync(Caller.Anonymous, new ListFolders()))).Code.Should().Be("folders.not-permitted");
        filing.Commands.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_command_that_names_the_version_it_read_loses_the_race_once_the_members_changed()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var read = (await filing.Services.ReadAsync(data.Minutes)).Version;

        // Somebody else changes the members: the document is at another version, though no column of its own changed.
        await filing.Services.ChangeAsync(data.Minutes, document => document.ShareWith(data.Hal, MemberPeriod.Open(data.Now), data.Now, by: data.Ada));

        var stale = new ShareDocument(data.Minutes, data.Hal, ExpectedVersion: read);
        var lost = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ada), provider => provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(stale, Cancellation).AsTask()))
            .Should().ThrowAsync<ConcurrencyConflictException>();
        (lost.Which.AggregateType, lost.Which.AggregateId).Should().Be((typeof(Document), data.Minutes));

        // A caller without access learns nothing from a version: answered as if it had sent none.
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => filing.Services.AsAsync(TestCallers.User(data.Ben), provider => provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(stale, Cancellation).AsTask()));
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => filing.Services.AsAsync(TestCallers.User(data.Eve), provider => provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(stale, Cancellation).AsTask()));
    }

    [Fact]
    public async Task What_is_written_and_read_for_one_names_no_table_of_the_other()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        string[] ofFolders = ["\"Folders\"", "\"" + FilingContext.FolderStaffTable + "\"", "\"" + FilingContext.FolderStaffRolesTable + "\""];
        string[] ofDocuments = ["\"Documents\"", "\"DocumentShares\"", "\"DocumentShareRoles\""];

        filing.Commands.Reset();
        await filing.Services.ChangeAsync(data.Minutes, document => document.ShareWith(data.Hal, DocumentMembership.Onlooker, MemberPeriod.Open(data.Now), data.Now, by: data.Ada));
        await filing.DocumentsAsync(TestCallers.User(data.Hal), access => access.KeysOnAsync([data.Minutes], FilingScenario.DocumentKeysAsked, Cancellation));
        filing.Commands.Commands.Should().NotBeEmpty().And.NotContain(sql => ofFolders.Any(table => sql.Contains(table, StringComparison.Ordinal)));

        filing.Commands.Reset();
        await filing.Services.AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var cabinet = await context.Folders.SingleAsync(folder => folder.Id == data.Cabinet, Cancellation);
            cabinet.Dismiss(data.Visitor);
            await context.SaveChangesAsync(Cancellation);
        });
        await filing.FoldersAsync(TestCallers.Staff(data.Clerk), access => access.KeysOnAsync([data.Cabinet], FilingScenario.FolderKeysAsked, Cancellation));
        filing.Commands.Commands.Should().NotBeEmpty().And.NotContain(sql => ofDocuments.Any(table => sql.Contains(table, StringComparison.Ordinal)));
    }
}
