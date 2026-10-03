using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.TestHost.Access;

// What the database itself lets a caller do with the application's resources, on a database that checks every
// row: thin rules of the application's own, which ask the set functions the package writes for each resource.
// They compile on any database, since a rule is only translated to SQL here. A resource's rules are the
// application's, and its member tables follow them, as every table of an aggregate's entities follows the
// aggregate's rules. What the package adds is a lock on top, written from the keys the resource's rules name:
// a member row is written only by who holds the key that changes the members, and the owner column only by
// who holds the key that changes the owner, whatever the rule below lets a caller change of the resource.

/// <summary>
/// The questions about documents the database answers: the two functions of a document's membership that rules
/// ask, under the names its rules give them and the owner the package gives them, the rules' name.
/// </summary>
[AccessFunctions(Owner = "documents")]
public static partial class DocumentQuestions
{
    /// <summary>The documents the caller sees.</summary>
    [AccessSet("documents_i_see")]
    public static partial AccessSet<DocumentId> Seen();

    /// <summary>The documents the caller holds <paramref name="key"/> on.</summary>
    /// <param name="key">A permission key.</param>
    [AccessSet("documents_where_i_hold")]
    public static partial AccessSet<DocumentId> HeldOn(string key);
}

/// <summary>The questions about folders the database answers, under the names the folder's rules give its functions.</summary>
[AccessFunctions(Owner = "folders")]
public static partial class FolderQuestions
{
    /// <summary>The folders the caller sees.</summary>
    [AccessSet("folder_ids_seen")]
    public static partial AccessSet<FolderId> Seen();

    /// <summary>The folders the caller holds <paramref name="key"/> on.</summary>
    /// <param name="key">A permission key.</param>
    [AccessSet("folder_ids_held")]
    public static partial AccessSet<FolderId> HeldOn(string key);
}

/// <summary>A user reads the documents it sees: those it is a member of now, and those it owns.</summary>
[RowAccess<Document>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class UsersReadTheDocumentsTheySee
{
    /// <summary>Whether the caller sees <paramref name="document"/>.</summary>
    public static bool Allows(Document document, Caller caller) => DocumentQuestions.Seen().Contains(document.Id);
}

/// <summary>
/// A user opens a document of its own: one that names it as its owner. Its share and the owner's role come
/// with it, and are written as the document's own rule lets a caller write them: the document is read through
/// what the caller sees, which its owner does from the moment its row is there. Not among the rules the suites
/// apply by default: a test that opens a document as its owner lists it.
/// </summary>
[RowAccess<Document>(RowOperations.Create, To = [RowAccessRoles.User])]
public static partial class UsersOpenDocumentsOfTheirOwn
{
    /// <summary>Whether the caller may add <paramref name="document"/>.</summary>
    public static bool Allows(Document document, Caller caller) => document.OwnerId.Value == caller.UserId;
}

/// <summary>
/// A user changes a document when it holds a key that changes one. Coarser than the application's own check on
/// purpose: a row knows no command, so the policy asks whether any of the keys is held, and the check in front
/// of the handler is the one that tells them apart. That check is not there for a statement that reaches the
/// database past the application, so the document's rules name the key that writes its shares and its owner
/// (<see cref="DocumentMembership"/>), and the database holds an editor to it: it changes the document, and
/// writes neither a share nor the owner.
/// </summary>
[RowAccess<Document>(RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class UsersChangeTheDocumentsTheyWorkOn
{
    /// <summary>Whether the caller may change <paramref name="document"/>.</summary>
    public static bool Allows(Document document, Caller caller)
        => DocumentQuestions.HeldOn(DocumentKeys.Edit).Contains(document.Id)
           || DocumentQuestions.HeldOn(DocumentKeys.Share).Contains(document.Id);
}

/// <summary>A member of staff reads the folders it is on.</summary>
[RowAccess<Folder>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class StaffReadTheFoldersTheyAreOn
{
    /// <summary>Whether the caller sees <paramref name="folder"/>.</summary>
    public static bool Allows(Folder folder, Caller caller) => FolderQuestions.Seen().Contains(folder.Id);
}
