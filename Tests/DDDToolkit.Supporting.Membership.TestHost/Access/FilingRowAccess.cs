using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.TestHost.Access;

// What the database itself lets a caller do with the application's resources, on a database that checks every
// row: thin rules of the application's own, which ask the set functions the package writes for each resource.
// They compile on any database, since a rule is only translated to SQL here. A resource's rules are the
// application's, and its member tables follow them, as every table of an aggregate's entities follows the
// aggregate's rules. What the package adds is a lock on top, written from the permissions the resource's rules name:
// a member row is written only by who holds the permission that changes the members, and the owner column only by
// who holds the permission that changes the owner, whatever the rule below lets a caller change of the resource.

/// <summary>
/// The questions about folders the database answers, under the names the package gives a folder's functions, from
/// the resource's name: a host may still ask the package's functions by name, as an <c>[AccessFunctions]</c> class
/// asks any function.
/// </summary>
[AccessFunctions(Owner = "folders")]
public static partial class FolderQuestions
{
    /// <summary>The folders the caller sees.</summary>
    [AccessSet("folders_i_see")]
    public static partial AccessSet<FolderId> FoldersISee(this IRowUser user);

    /// <summary>The folders the caller holds <paramref name="permission"/> on.</summary>
    /// <param name="permission">A permission.</param>
    [AccessSet("folders_where_i_hold")]
    public static partial AccessSet<FolderId> FoldersWhereIHold(this IRowUser user, string permission);
}

/// <summary>
/// A user reads the documents it sees: those it is a member of now, and those it owns. Asked by the document's id: no
/// function's name is said, and the export writes the policy with the function the package writes for the documents,
/// whatever their rules call it.
/// </summary>
[RowAccess<Document>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class UsersReadTheDocumentsTheySee
{
    /// <summary>Whether the caller sees <paramref name="document"/>.</summary>
    public static bool Allows(Document document, IRowUser user) => user.Sees<DocumentId>().Contains(document.Id);
}

/// <summary>
/// A user opens a document of its own: one that names it as its owner. Its share, which holds no role, comes
/// with it, and are written as the document's own rule lets a caller write them: the document is read through
/// what the caller sees, which its owner does from the moment its row is there. Not among the rules the suites
/// apply by default: a test that opens a document as its owner lists it.
/// </summary>
[RowAccess<Document>(RowOperations.Create, To = [RowAccessRoles.User])]
public static partial class UsersOpenDocumentsOfTheirOwn
{
    /// <summary>Whether the caller may add <paramref name="document"/>.</summary>
    public static bool Allows(Document document, IRowUser user) => document.OwnerId.Value == user.Id;
}

/// <summary>
/// A user changes a document when it holds a permission that changes one. Coarser than the application's own check on
/// purpose: a row knows no command, so the policy asks whether any of the permissions is held, and the check in front
/// of the handler is the one that tells them apart. That check is not there for a statement that reaches the
/// database past the application, so the document's rules name the permission that writes its shares and its owner
/// (<see cref="DocumentMembership"/>), and the database holds an editor to it: it changes the document, and
/// writes neither a share nor the owner.
/// </summary>
[RowAccess<Document>(RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class UsersChangeTheDocumentsTheyWorkOn
{
    /// <summary>Whether the caller may change <paramref name="document"/>.</summary>
    public static bool Allows(Document document, IRowUser user)
        => user.Holds<DocumentId>(DocumentPermissions.Edit).Contains(document.Id)
           || user.Holds<DocumentId>(DocumentPermissions.Share).Contains(document.Id);
}

/// <summary>A member of staff reads the folders it is on.</summary>
[RowAccess<Folder>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class StaffReadTheFoldersTheyAreOn
{
    /// <summary>Whether the caller sees <paramref name="folder"/>.</summary>
    public static bool Allows(Folder folder, IRowUser user) => user.FoldersISee().Contains(folder.Id);
}

/// <summary>
/// An archivist, a caller of the token role <c>archivist</c>, reads the folders it holds the permission that reads one on:
/// the one rule for that role. The folders' functions are granted to it because this rule is written for it, and to
/// no role that no rule of a folder is for. Not among the rules the suites apply by default: a test of the grants
/// lists it.
/// </summary>
[RowAccess<Folder>(RowOperations.Read, To = [Archivists.Role])]
public static partial class ArchivistsReadTheFoldersTheyHold
{
    /// <summary>Whether the caller holds the permission that reads <paramref name="folder"/>.</summary>
    public static bool Allows(Folder folder, IRowUser user) => user.FoldersWhereIHold(FolderPermissions.Read).Contains(folder.Id);
}

/// <summary>The token role of the host's archivists.</summary>
public static class Archivists
{
    /// <summary>The role, as a rule names it.</summary>
    public const string Role = RowAccessRoles.TokenPrefix + "archivist";
}
