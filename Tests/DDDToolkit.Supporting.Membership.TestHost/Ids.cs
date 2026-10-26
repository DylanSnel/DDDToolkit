using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.TestHost;

// The application's ids. A folder's is a long and a folder's members are known by a text, to prove the
// package forces neither a Guid nor one kind of member on anyone.

/// <summary>A user: who can be a member of a document.</summary>
[EntityId<Guid>]
public readonly partial record struct UserId;

/// <summary>A member of staff, by the code the identity provider gives them: who can be a member of a folder.</summary>
[EntityId<string>]
public readonly partial record struct StaffCode;

/// <summary>A document's id.</summary>
[EntityId<Guid>]
public readonly partial record struct DocumentId;

/// <summary>The id of a document's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct DocumentShareId;

/// <summary>A folder's id.</summary>
[EntityId<long>]
public readonly partial record struct FolderId;

/// <summary>The id of a folder's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct FolderMemberId;
