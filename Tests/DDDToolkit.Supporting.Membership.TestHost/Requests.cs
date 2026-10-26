using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.TestHost;

/// <summary>
/// What every request of the host's one module implements. The module has two kinds of resource with members,
/// so its requests declare requirements about documents and about folders, and both checks sit in its one set.
/// <para>
/// Who may change the members of a resource is the host's to decide, and it decides it differently for its two
/// resources. A document is shared by whoever holds one key on it, and that is all: one line on the request
/// (<see cref="ShareDocument"/>). Staff are put on a folder by whoever holds a key on it as well, and then a
/// rule the host wrote itself applies, which reads until when the caller holds that key
/// (<see cref="AdmitStaff"/>, <see cref="AdmitStaffHandler"/>).
/// </para>
/// <para>
/// Each request says what it requires with Membership's attributes, and the toolkit's generator writes the call each
/// stands for into this interface, which is marked and partial for that: <c>[RequiresKey(key)]</c> with
/// <c>[OnResource]</c> on the resource and <c>[ExpectedVersion]</c> on the version the caller read, and
/// <c>[SeenWith&lt;TResourceId&gt;(key)]</c> on a list.
/// </para>
/// </summary>
[AccessRequests]
public partial interface IFilingRequest : IRequireAccess;

/// <summary>
/// Shares a document with a user, in a role or in none, until a moment or with no end. Whoever holds the key
/// that shares, on that document, may: the attribute below is the whole of this host's rule about it.
/// </summary>
[RequiresKey(DocumentPermissions.Share)]
public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, NamedRole? Role = null, DateTimeOffset? Until = null, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>
/// Puts a member of staff on a folder, in a role, until a moment or with no end. The key that decides a
/// folder's staff is required on it, and the handler adds the host's own rule: for no longer than the caller
/// holds that key.
/// </summary>
[RequiresKey(FolderPermissions.Staff)]
public sealed record AdmitStaff([OnResource] FolderId Folder, StaffCode Staff, NamedRole Role, DateTimeOffset? Until = null, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>
/// Takes a user off a document: a member takes itself off a document it sees, or whoever holds the key that shares it
/// takes anybody off. Yours, or the key on the resource: the caller's own <see cref="UserId"/> is what
/// <see cref="CallersUser"/> says, and either way the document is read at the door, its version compared, and what the
/// expert hold holds the save to kept.
/// </summary>
[RequiresKey(DocumentPermissions.Share)]
public sealed record LeaveDocument([OnResource] DocumentId Document, [OrOwn] UserId Member, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>Lists the documents the caller sees: nothing is refused, the statement filters.</summary>
[SeenWith<DocumentId>(DocumentPermissions.View)]
public sealed record ListDocuments : IFilingRequest;

/// <summary>Puts something in a folder: requires the key that files, on that folder.</summary>
[RequiresKey(FolderPermissions.File)]
public sealed record FileInFolder([OnResource] FolderId Folder, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

/// <summary>Lists the folders the caller sees.</summary>
[SeenWith<FolderId>(FolderPermissions.Read)]
public sealed record ListFolders : IFilingRequest;
