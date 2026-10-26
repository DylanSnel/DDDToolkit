using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// An application has several kinds of resource with members, and here two of them sit in one project: a
/// document shared with users, and a folder with staff on it. Each has its own member class, its own member
/// ids, its own roles, codes and guard, and neither touches the other.
/// </summary>
public sealed class TwoResourcesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId Author = UserId.CreateSequential();

    private static readonly StaffCode Keeper = new("K-001");

    private static readonly StaffCode Clerk = new("C-014");

    [Fact]
    public void Each_resource_keeps_the_same_rules_under_its_own_codes()
    {
        var document = new Document(DocumentId.CreateSequential(), "Minutes", Author, DocumentMembership.Owner, Now);
        var folder = new Folder(new FolderId(7), Keeper, FolderMembership.Keeper, Now);
        var reader = UserId.CreateSequential();
        document.ShareWith(reader, MemberPeriod.Open(Now), Now, by: Author);
        folder.Admit(Clerk, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: Keeper);

        // The same rule, broken on each: the document says it in the package's words under its prefix, the
        // folder in words of its own. The kind is the rule's, whatever it is called.
        var shared = Refused.With(DocumentRefusals.Membership, MembershipRefusals.AlreadyMember, () => document.ShareWith(reader, MemberPeriod.Open(Now), Now, by: Author));
        var staffed = Refused.With(FolderRefusals.Membership, MembershipRefusals.AlreadyMember, () => folder.Admit(Clerk, FolderMembership.Visitor, MemberPeriod.Open(Now), Now, by: Keeper));
        (shared.Code, staffed.Code).Should().Be(("documents.already-member", "folders.already-on-folder"));
        (shared.Kind, staffed.Kind).Should().Be((RefusalKind.Conflict, RefusalKind.Conflict));
        shared.Arguments.Should().Contain("Member", reader);
        staffed.Arguments.Should().Contain("Staff", Clerk);

        Refused.With(DocumentRefusals.Membership, MembershipRefusals.MemberNotFound, () => document.Unshare(UserId.CreateSequential())).Code.Should().Be("documents.member-not-found");
        Refused.With(FolderRefusals.Membership, MembershipRefusals.MemberNotFound, () => folder.Dismiss(new StaffCode("X-999"))).Code.Should().Be("folders.not-on-folder");

        // A rule the folder did not rename keeps the package's name, under the folder's prefix.
        Refused.With(FolderRefusals.Membership, MembershipRefusals.OwnerProtected, () => folder.Dismiss(Keeper)).Code.Should().Be("folders.owner-protected");
        Refused.With(DocumentRefusals.Membership, MembershipRefusals.OwnerProtected, () => document.Unshare(Author)).Code.Should().Be("documents.owner-protected");
    }

    [Fact]
    public async Task Each_resource_carries_a_member_under_its_own_word_for_it_whoever_refuses()
    {
        var document = new Document(DocumentId.CreateSequential(), "Minutes", Author, DocumentMembership.Owner, Now);
        var folder = new Folder(new FolderId(7), Keeper, FolderMembership.Keeper, Now);
        var reader = UserId.CreateSequential();
        var stranger = new StaffCode("X-999");
        document.ShareWith(reader, DocumentMembership.Onlooker, MemberPeriod.Open(Now), Now, by: Author);
        folder.Admit(Clerk, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: Keeper);

        // The member list of a folder, in every refusal that names a member: a client reads "Staff", never "Member".
        RefusalException[] onFolder =
        [
            Refused.With(FolderRefusals.Membership, MembershipRefusals.AlreadyMember, () => folder.Admit(Clerk, FolderMembership.Visitor, MemberPeriod.Open(Now), Now, by: Keeper)),
            Refused.With(FolderRefusals.Membership, MembershipRefusals.RoleHeld, () => folder.GiveRole(Clerk, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: Keeper)),
            Refused.With(FolderRefusals.Membership, MembershipRefusals.RoleNotHeld, () => folder.TakeRole(Clerk, FolderMembership.Visitor, FolderMembership.Keeper)),
        ];
        onFolder.Should().OnlyContain(refusal => Equals(refusal.Arguments["Staff"], Clerk) && !refusal.Arguments.ContainsKey("Member"));
        onFolder[1].Arguments.Should().Contain("Role", FolderMembership.Clerk, "only the member has a word of the folder's own");

        Refused.With(FolderRefusals.Membership, MembershipRefusals.MemberNotFound, () => folder.Dismiss(stranger)).Arguments.Keys.Should().Equal("Staff");
        Refused.With(FolderRefusals.Membership, MembershipRefusals.MemberNotFound, () => folder.GiveRole(stranger, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: Keeper)).Arguments.Keys.Should().Equal("Staff");
        Refused.With(FolderRefusals.Membership, MembershipRefusals.MemberNotFound, () => folder.TakeRole(stranger, FolderMembership.Clerk, FolderMembership.Keeper)).Arguments.Keys.Should().Equal("Staff");

        // The admission of folders, which refuses under the same codes, says it the same way.
        var admission = new MemberAdmission<FolderId, StaffCode, NamedRole>(FolderMembership.Rules, new NamedRoles<FolderId>(FolderMembership.Rules), new NobodyIsActive());
        var inactive = await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.MemberNotActive, () => admission.RequireMemberAsync(stranger, TestContext.Current.CancellationToken).AsTask());
        inactive.Arguments.Should().Contain("Staff", stranger).And.NotContainKey("Member");

        // And so does the rule about the whole list, when it is broken behind the member list.
        Break.ListOf<FolderMember>(folder, "_staff").Add(new Folder(new FolderId(8), Clerk, FolderMembership.Keeper, Now).Staff.Single());
        var twice = folder.GetInvariantViolations().Should().ContainSingle().Which;
        twice.Arguments.Should().Contain("Staff", Clerk).And.NotContainKey("Member");

        // A document has no word of its own: the package's.
        Refused.With(DocumentRefusals.Membership, MembershipRefusals.RoleHeld, () => document.GiveRole(reader, DocumentMembership.Onlooker, MemberPeriod.Open(Now), Now, by: Author))
            .Arguments.Should().Contain("Member", reader).And.NotContainKey("Staff");
        (DocumentRefusals.Membership.MemberArgument, FolderRefusals.Membership.MemberArgument).Should().Be(("Member", "Staff"));
    }

    [Fact]
    public void The_rule_a_member_checks_itself_names_the_member_in_the_packages_word_on_every_resource()
    {
        // A member does not know whose it is, so neither the code nor the argument is the folder's.
        var folder = new Folder(new FolderId(7), Keeper, FolderMembership.Keeper, Now);
        var clerk = folder.Admit(Clerk, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: Keeper);
        var second = new Folder(new FolderId(8), Keeper, FolderMembership.Keeper, Now).Admit(Clerk, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: Keeper).Roles.Single();
        Break.ListOf<MemberRole<StaffCode, NamedRole>>(clerk, "_roles").Add(second);

        var violation = folder.GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be(MembershipRefusals.RoleHeld);
        violation.Arguments.Should().Contain(MembershipCodes.DefaultMemberArgument, Clerk).And.Contain("Role", FolderMembership.Clerk).And.NotContainKey("Staff");
    }

    [Fact]
    public void Each_resource_has_members_roles_and_an_owner_of_its_own()
    {
        var document = new Document(DocumentId.CreateSequential(), "Minutes", Author, DocumentMembership.Owner, Now);
        var folder = new Folder(new FolderId(7), Keeper, FolderMembership.Keeper, Now);

        // A document's owner holds the role its rules added; a folder's keeper the one its rules declare.
        document.Shares.Single().Roles.Single().RoleId.Should().Be(new NamedRole("owner"));
        folder.Staff.Single().Roles.Single().RoleId.Should().Be(new NamedRole("keeper"));
        document.Shares.Single().MemberId.Should().BeOfType<UserId>();
        folder.Staff.Single().MemberId.Should().BeOfType<StaffCode>().Which.Value.Should().Be("K-001");

        folder.Admit(Clerk, FolderMembership.Clerk, MemberPeriod.Between(Now, Now.AddDays(30)), Now, by: Keeper);
        folder.HandOver(Clerk, FolderMembership.Keeper, Now.AddDays(1));

        folder.Keeper.Should().Be(Clerk);
        folder.Staff.Single(member => member.MemberId == Clerk).EndsAt.Should().BeNull("a keeper's place does not run out");
        folder.Staff.Single(member => member.MemberId == Keeper).Roles.Should().BeEmpty("the old keeper lost the keeper's role, and held no other");
        document.OwnerId.Should().Be(Author, "what happens on a folder says nothing about a document");
        folder.GetInvariantViolations().Should().BeEmpty();
        document.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void Each_resource_puts_its_own_guard_in_front_of_its_member_list()
    {
        var document = new Document(DocumentId.CreateSequential(), "Minutes", Author, DocumentMembership.Owner, Now);
        var folder = new Folder(new FolderId(7), Keeper, FolderMembership.Keeper, Now);
        folder.Lock();

        // A locked folder admits nobody, under the folder's own code; a document knows no lock.
        FluentActions.Invoking(() => folder.Admit(Clerk, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: Keeper))
            .Should().Throw<RefusalException>().Which.Code.Should().Be(FolderRefusals.Locked);
        document.ShareWith(UserId.CreateSequential(), MemberPeriod.Open(Now), Now, by: Author);

        document.Shares.Should().HaveCount(2);
        folder.Staff.Should().ContainSingle();
    }

    [Fact]
    public void A_rule_broken_behind_the_member_list_is_reported_under_the_code_of_the_resource_it_is_broken_on()
    {
        var folder = new Folder(new FolderId(7), Keeper, FolderMembership.Keeper, Now);
        var other = new Folder(new FolderId(8), Keeper, FolderMembership.Keeper, Now);
        Break.ListOf<FolderMember>(folder, "_staff").Add(other.Staff.Single());

        folder.GetInvariantViolations().Should().ContainSingle().Which.Code.Should().Be("folders.already-on-folder");

        Break.ListOf<FolderMember>(folder, "_staff").Clear();
        folder.GetInvariantViolations().Should().ContainSingle().Which.Code.Should().Be("folders.owner-protected");
    }

    /// <summary>A directory that knows nobody as active.</summary>
    private sealed class NobodyIsActive : IMemberDirectory<FolderId, StaffCode>
    {
        public ValueTask<bool> IsActiveAsync(StaffCode member, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    [Fact]
    public void The_codes_of_two_resources_have_nothing_in_common()
    {
        var documents = DocumentMembership.Rules.Codes;
        var folders = FolderMembership.Rules.Codes;

        documents.All.Should().HaveCount(MembershipRefusals.Codes.Count).And.OnlyHaveUniqueItems().And.OnlyContain(code => code.StartsWith("documents.", StringComparison.Ordinal));
        folders.All.Should().HaveCount(MembershipRefusals.Codes.Count).And.OnlyHaveUniqueItems().And.OnlyContain(code => code.StartsWith("folders.", StringComparison.Ordinal));
        documents.All.Should().NotIntersectWith(folders.All);

        // The rules carry the codes the aggregate refuses with: one declaration, handed to both.
        documents.Should().BeSameAs(DocumentRefusals.Membership);
        folders.Should().BeSameAs(FolderRefusals.Membership);
    }

    [Fact]
    public void The_rules_of_one_resource_say_nothing_about_the_other()
    {
        var documents = DocumentMembership.Rules;
        var folders = FolderMembership.Rules;

        documents.MembersHold(DocumentKeys.Edit).Should().BeTrue();
        documents.MembersHold(FolderKeys.File).Should().BeFalse("a document's roles give a document's keys");
        documents.RolesWith(FolderKeys.Read).Should().BeEmpty();
        documents.Knows(FolderMembership.Clerk).Should().BeFalse();
        folders.Knows(DocumentMembership.Contributor).Should().BeFalse();
        folders.RolesWith(DocumentKeys.View).Should().BeEmpty();

        (documents.Members.Kind, folders.Members.Kind).Should().Be((MemberSourceKind.CallerId, MemberSourceKind.Claim));
        (documents.OwnerRole, folders.OwnerRole).Should().Be(("owner", "keeper"));
        documents.Functions.Should().NotBe(folders.Functions);
    }
}
