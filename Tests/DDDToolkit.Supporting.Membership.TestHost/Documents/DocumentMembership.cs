namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>The keys a document is asked about.</summary>
public static class DocumentKeys
{
    /// <summary>See a document.</summary>
    public const string View = "documents.view";

    /// <summary>Change a document.</summary>
    public const string Edit = "documents.edit";

    /// <summary>Decide who a document is shared with: the key this host's commands that change the shares require.</summary>
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
/// The rules of access through a document's members, with as little said as a resource can: its name, its
/// keys, its roles, and what being a member gives. Who may share a document is not said here: the requests
/// that do say which key they require. That key is named once more, for the database: a document's rows are
/// checked there, and the rule that lets a caller change a document lets whoever edits one, so the database
/// is told which key writes the shares and the owner.
/// </summary>
public static class DocumentMembership
{
    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = new(
        "documents",
        keys: [DocumentKeys.View, DocumentKeys.Edit, DocumentKeys.Share],
        roles: [new("contributor", [DocumentKeys.View, DocumentKeys.Edit]), new("onlooker", [DocumentKeys.View])],
        seeKey: DocumentKeys.View,
        codes: DocumentRefusals.Membership,
        changeMembersKey: DocumentKeys.Share,
        changeOwnerKey: DocumentKeys.Share);

    /// <summary>The role that changes a document.</summary>
    public static NamedRole Contributor { get; } = new("contributor");

    /// <summary>The role that only reads.</summary>
    public static NamedRole Onlooker { get; } = new("onlooker");

    /// <summary>The owner's role, which the rules add, with every key of a document: nobody declared one.</summary>
    public static NamedRole Owner { get; } = new(MembershipRules.DefaultOwnerRole);
}
