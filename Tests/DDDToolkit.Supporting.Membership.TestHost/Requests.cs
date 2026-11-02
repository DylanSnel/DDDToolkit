using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.TestHost;

/// <summary>
/// What every request of the host's one module implements. The module has two kinds of resource with members,
/// so its requests declare requirements about documents and about folders, and both checks sit in its one set.
/// <para>
/// Who may change the members of a resource is the host's to decide, and it decides it differently for its two
/// resources. A document is shared by whoever holds one permission on it, and that is all: one line on the request
/// (<see cref="ShareDocument"/>). Staff are put on a folder by whoever holds a permission on it as well, and then a
/// rule the host wrote itself applies, which reads until when the caller holds that permission
/// (<see cref="AdmitStaff"/>, <see cref="AdmitStaffHandler"/>).
/// </para>
/// <para>
/// Each request says what it requires with Membership's attributes, and the toolkit's generator writes the call each
/// stands for into this interface, which is marked and partial for that: <c>[OnResource(permission)]</c> on the resource
/// and <c>[ExpectedVersion]</c> on the version the caller read, and <c>[SeenWith&lt;TResourceId&gt;(permission)]</c> on a
/// list. A request with a marker on each of two resources requires both, <c>AccessRequirement.All</c> of the two
/// (<see cref="FileDocumentInFolder"/>, <see cref="ShareOnBoth"/>).
/// </para>
/// </summary>
[AccessRequests]
public partial interface IFilingRequest : IRequireAccess;

/// <summary>
/// Shares a document with a user, in a role or in none, until a moment or with no end. Whoever holds the permission
/// that shares, on that document, may: the attribute below is the whole of this host's rule about it.
/// </summary>
public sealed record ShareDocument([OnResource(DocumentPermissions.Share)] DocumentId Document, UserId With, NamedRole? Role = null, DateTimeOffset? Until = null, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>
/// Puts a member of staff on a folder, in a role, until a moment or with no end. The permission that decides a
/// folder's staff is required on it, and the handler adds the host's own rule: for no longer than the caller
/// holds that permission.
/// </summary>
public sealed record AdmitStaff([OnResource(FolderPermissions.Staff)] FolderId Folder, StaffCode Staff, NamedRole Role, DateTimeOffset? Until = null, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>
/// Takes a user off a document: a member takes itself off a document it sees, or whoever holds the permission that shares it
/// takes anybody off. Yours, or the permission on the resource: the caller's own <see cref="UserId"/> is what
/// <see cref="CurrentUserId"/> says, and either way the document is read at the door, its version compared, and what the
/// expert hold holds the save to kept.
/// </summary>
public sealed record LeaveDocument([OnResource(DocumentPermissions.Share)] DocumentId Document, [OrOwn] UserId Member, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>Lists the documents the caller sees: nothing is refused, the statement filters.</summary>
[SeenWith<DocumentId>(DocumentPermissions.View)]
public sealed record ListDocuments : IFilingRequest;

/// <summary>Puts something in a folder: requires the permission that files, on that folder.</summary>
public sealed record FileInFolder([OnResource(FolderPermissions.File)] FolderId Folder, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>Lists the folders the caller sees.</summary>
[SeenWith<FolderId>(FolderPermissions.Read)]
public sealed record ListFolders : IFilingRequest;

/// <summary>
/// Files a document in a folder: the permission that edits, on the document, and the permission that files, on the folder.
/// Two kinds of resource, each decided by its own check. The version the caller read is the document's, so the document's
/// check compares it, and the folder is only read.
/// </summary>
public sealed record FileDocumentInFolder(
    [OnResource(DocumentPermissions.Edit)] DocumentId Document,
    [OnResource(FolderPermissions.File)] FolderId Folder,
    [ExpectedVersion<Document>] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>Shares a user on each of two documents: the permission that shares, on both. Two resources of one kind.</summary>
public sealed record ShareOnBoth(
    [OnResource(DocumentPermissions.Share)] DocumentId First,
    [OnResource(DocumentPermissions.Share)] DocumentId Second,
    UserId With) : IFilingRequest;
