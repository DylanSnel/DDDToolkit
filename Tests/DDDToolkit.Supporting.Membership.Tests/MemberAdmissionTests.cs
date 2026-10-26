using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// What a use case asks before it changes a resource's members, where the member list cannot answer: whether
/// somebody may be made a member, whether a role goes to a member, and which role an owner holds. One
/// admission per kind of resource, each refusing under its own resource's codes.
/// </summary>
public sealed class MemberAdmissionTests
{
    private static readonly UserId Active = UserId.CreateSequential();

    private static readonly UserId Left = UserId.CreateSequential();

    private static MemberAdmission<DocumentId, UserId, NamedRole> Documents(
        IMemberDirectory<DocumentId, UserId>? directory = null,
        IMemberRolePolicy<DocumentId, NamedRole>? policy = null)
        => new(DocumentMembership.Rules, new NamedRoles<DocumentId>(DocumentMembership.Rules), directory, policy);

    private static MemberAdmission<FolderId, StaffCode, NamedRole> Folders()
        => new(FolderMembership.Rules, new NamedRoles<FolderId>(FolderMembership.Rules));

    // ---------------------------------------------------------------- who may be a member

    [Fact]
    public async Task Without_a_directory_everybody_may_be_made_a_member()
    {
        var admission = Documents();

        await admission.RequireMemberAsync(Active, TestContext.Current.CancellationToken);
        await admission.RequireMemberAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Somebody_the_directory_does_not_know_as_active_is_not_made_a_member()
    {
        var directory = new Directory(Active);
        var admission = Documents(directory);

        await admission.RequireMemberAsync(Active, TestContext.Current.CancellationToken);

        var refusal = await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.MemberNotActive,
            () => admission.RequireMemberAsync(Left, TestContext.Current.CancellationToken).AsTask());
        refusal.Code.Should().Be("documents.member-not-active");
        refusal.Kind.Should().Be(RefusalKind.Conflict);
        refusal.Arguments.Should().Contain("Member", Left);
        directory.Asked.Should().Equal(Active, Left);
    }

    // ---------------------------------------------------------------- which role goes to a member

    [Fact]
    public async Task A_role_the_rules_do_not_declare_does_not_go_to_a_member()
    {
        var admission = Documents();

        await admission.RequireRoleAsync(DocumentMembership.Contributor, TestContext.Current.CancellationToken);
        await admission.RequireRoleAsync(DocumentMembership.Onlooker, TestContext.Current.CancellationToken);

        // The owner holds no role, so none is added for it: a role called so is one the rules do not declare.
        await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => admission.RequireRoleAsync(new NamedRole("owner"), TestContext.Current.CancellationToken).AsTask());

        var unknown = new NamedRole("auditor");
        var refusal = await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => admission.RequireRoleAsync(unknown, TestContext.Current.CancellationToken).AsTask());
        refusal.Kind.Should().Be(RefusalKind.Invalid);
        refusal.Arguments.Should().Contain("Role", unknown);
        refusal.Arguments[RefusalException.FieldArgument].Should().Be("role", "an Invalid refusal names the input it is about");

        await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => admission.RequireRoleAsync(default, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task A_role_the_applications_own_rule_keeps_off_does_not_go_to_a_member()
    {
        var policy = new Policy(allowed: DocumentMembership.Onlooker);
        var admission = Documents(policy: policy);

        await admission.RequireRoleAsync(DocumentMembership.Onlooker, TestContext.Current.CancellationToken);
        await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => admission.RequireRoleAsync(DocumentMembership.Contributor, TestContext.Current.CancellationToken).AsTask());

        // A role that does not exist is refused before the application's rule is asked about it.
        await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => admission.RequireRoleAsync(new NamedRole("auditor"), TestContext.Current.CancellationToken).AsTask());
        policy.Asked.Should().Equal(DocumentMembership.Onlooker, DocumentMembership.Contributor);
    }

    // ---------------------------------------------------------------- one admission per resource

    [Fact]
    public async Task Each_resource_admits_by_its_own_rules_and_refuses_under_its_own_codes()
    {
        var documents = Documents();
        var folders = Folders();

        await documents.RequireRoleAsync(DocumentMembership.Contributor, TestContext.Current.CancellationToken);
        await folders.RequireRoleAsync(FolderMembership.Clerk, TestContext.Current.CancellationToken);

        // The roles are named roles on both, and still a document's role is none of a folder's.
        var onFolder = await Refused.WithCodeAsync(
            FolderRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => folders.RequireRoleAsync(DocumentMembership.Contributor, TestContext.Current.CancellationToken).AsTask());
        var onDocument = await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => documents.RequireRoleAsync(FolderMembership.Clerk, TestContext.Current.CancellationToken).AsTask());
        (onFolder.Code, onDocument.Code).Should().Be(("folders.role-not-for-members", "documents.role-not-for-members"));
    }

    [Fact]
    public void An_admission_is_made_of_its_resources_rules_and_roles()
    {
        FluentActions.Invoking(() => new MemberAdmission<DocumentId, UserId, NamedRole>(null!, new NamedRoles<DocumentId>(DocumentMembership.Rules)))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new MemberAdmission<DocumentId, UserId, NamedRole>(DocumentMembership.Rules, null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new NamedRoles<DocumentId>(null!)).Should().Throw<ArgumentNullException>();
    }

    /// <summary>The application's users: the ones that are active.</summary>
    private sealed class Directory(params UserId[] active) : IMemberDirectory<DocumentId, UserId>
    {
        public List<UserId> Asked { get; } = [];

        public ValueTask<bool> IsActiveAsync(UserId member, CancellationToken cancellationToken)
        {
            Asked.Add(member);
            return ValueTask.FromResult(active.Contains(member));
        }
    }

    /// <summary>The application's own rule about which roles go on a document.</summary>
    private sealed class Policy(params NamedRole[] allowed) : IMemberRolePolicy<DocumentId, NamedRole>
    {
        public List<NamedRole> Asked { get; } = [];

        public ValueTask<bool> MayHoldAsync(NamedRole role, CancellationToken cancellationToken)
        {
            Asked.Add(role);
            return ValueTask.FromResult(allowed.Contains(role));
        }
    }
}
