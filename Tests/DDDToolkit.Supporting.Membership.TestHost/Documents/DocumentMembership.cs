using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>
/// The permissions a document is asked about. The owner holds all three by owning it, every member sees it, and the
/// one that decides who a document is shared with is also the one the database holds the shares and the owner to:
/// a document's rows are checked there, and the rule that lets a caller change a document lets whoever edits one.
/// </summary>
[Permissions]
public static class DocumentPermissions
{
    /// <summary>See a document.</summary>
    [On<Document>(EveryMember = true)]
    public const string View = "documents.view";

    /// <summary>Change a document.</summary>
    [On<Document>]
    public const string Edit = "documents.edit";

    /// <summary>Decide who a document is shared with: the permission this host's commands that change the shares require.</summary>
    [On<Document>(LocksMembers = true, LocksOwner = true)]
    public const string Share = "documents.share";
}

/// <summary>What a document refuses with: its own code, and the codes of its members, where the aggregate can reach them.</summary>
public static class DocumentRefusals
{
    /// <summary>The document is archived, and an archived document changes no more.</summary>
    public const string Archived = "documents.archived";

    /// <summary>The codes of the rules about a document's members: the package's names, under the host's prefix.</summary>
    public static MembershipCodes Membership { get; } = MembershipCodes.Under("documents");
}

/// <summary>
/// The rules of access through a document's members, with little said: its roles, and that no system work in a scope
/// holds anything on a document, <c>systemScopes: []</c>, where the module's own would otherwise. Its name, its codes
/// and its permissions come through its type. Who may share a document is not said here: the requests that do say
/// which permission they require.
/// </summary>
public static class DocumentMembership
{
    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = MembershipRules.For<Document>(
        Roles.Fixed(
        [
            new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit]),
            new("onlooker", [DocumentPermissions.View]),
            new("manager", [DocumentPermissions.View, DocumentPermissions.Edit, DocumentPermissions.Share]),
        ]),
        systemScopes: []);

    /// <summary>The role that changes a document.</summary>
    public static NamedRole Contributor { get; } = new("contributor");

    /// <summary>The role that only reads.</summary>
    public static NamedRole Onlooker { get; } = new("onlooker");

    /// <summary>The role that gives every permission on a document: what its owner holds by owning it, given to a member.</summary>
    public static NamedRole Manager { get; } = new("manager");
}
