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
/// </summary>
public interface IFilingRequest : IRequireAccess;

/// <summary>
/// Shares a document with a user, in a role or in none, until a moment or with no end. Whoever holds the key
/// that shares, on that document, may: the line below is the whole of this host's rule about it.
/// </summary>
public sealed record ShareDocument(DocumentId Document, UserId With, NamedRole? Role = null, DateTimeOffset? Until = null, long? ExpectedVersion = null) : IFilingRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(DocumentKeys.Share, Document, ExpectedVersion);
}

/// <summary>
/// Puts a member of staff on a folder, in a role, until a moment or with no end. The key that decides a
/// folder's staff is required on it, and the handler adds the host's own rule: for no longer than the caller
/// holds that key.
/// </summary>
public sealed record AdmitStaff(FolderId Folder, StaffCode Staff, NamedRole Role, DateTimeOffset? Until = null, long? ExpectedVersion = null) : IFilingRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(FolderKeys.Staff, Folder, ExpectedVersion);
}

/// <summary>Lists the documents the caller sees: nothing is refused, the statement filters.</summary>
public sealed record ListDocuments : IFilingRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<DocumentId>(DocumentKeys.View);
}

/// <summary>Puts something in a folder: requires the key that files, on that folder.</summary>
public sealed record FileInFolder(FolderId Folder, long? ExpectedVersion = null) : IFilingRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.On(FolderKeys.File, Folder, ExpectedVersion);
}

/// <summary>Lists the folders the caller sees.</summary>
public sealed record ListFolders : IFilingRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<FolderId>(FolderKeys.Read);
}
