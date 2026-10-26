using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>A document shared with a user, as the package declares a member: one line, which names the document it is a member of.</summary>
[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;

/// <summary>
/// A document, shared with users: the first of the host's two kinds of resource with members. The aggregate is
/// the host's own, declared as it always was. It keeps its owner and its shares, holds a member list over them,
/// puts its own guard in front of every change, and raises its own events from what the member list answers.
/// </summary>
[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    /// <summary>Writes a document, with <paramref name="owner"/> a member from <paramref name="now"/> on, for good, in the owner's role.</summary>
    public Document(DocumentId id, string title, UserId owner, NamedRole ownerRole, DateTimeOffset now) : base(id)
    {
        Title = title;
        OwnerId = owner;
        Members.Open(ownerRole, now);
        RaiseDomainEvent(new DocumentWritten(id, owner));
    }

    /// <summary>The document's title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Whether the document is archived: an archived document changes no more.</summary>
    public bool Archived { get; private set; }

    /// <summary>The user that owns the document.</summary>
    public UserId OwnerId { get; private set; }

    /// <summary>Who the document is shared with: the collection the member list works on.</summary>
    public partial IReadOnlyList<DocumentShare> Shares { get; }

    /// <summary>
    /// The codes the rules about a document's shares refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => DocumentRefusals.Membership;

    /// <summary>Shares the document with a user, with no role yet.</summary>
    public DocumentShare ShareWith(UserId with, MemberPeriod period, DateTimeOffset now, UserId? by)
    {
        RequireNotArchived();
        var share = Members.Add(with, period, now, by);
        RaiseDomainEvent(new DocumentShared(Id, with));
        return share;
    }

    /// <summary>Shares the document with a user in a role, both for the same period.</summary>
    public DocumentShare ShareWith(UserId with, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by)
    {
        RequireNotArchived();
        var share = Members.Add(with, role, period, now, by);
        RaiseDomainEvent(new DocumentShared(Id, with));
        RaiseDomainEvent(new DocumentRoleGiven(Id, with, role));
        return share;
    }

    /// <summary>Gives a user the document is shared with a role.</summary>
    public MemberRole<UserId, NamedRole> GiveRole(UserId to, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by)
    {
        RequireNotArchived();
        var given = Members.GiveRole(to, role, period, now, by);
        RaiseDomainEvent(new DocumentRoleGiven(Id, to, role));
        return given;
    }

    /// <summary>Takes a role from a user the document is shared with.</summary>
    public void TakeRole(UserId from, NamedRole role, NamedRole? ownerRole)
    {
        RequireNotArchived();
        Members.TakeRole(from, role, ownerRole);
        RaiseDomainEvent(new DocumentRoleTaken(Id, from, role));
    }

    /// <summary>Stops sharing the document with a user.</summary>
    public void Unshare(UserId user)
    {
        RequireNotArchived();
        Members.Remove(user);
        RaiseDomainEvent(new DocumentUnshared(Id, user));
    }

    /// <summary>Hands the document to another owner, and raises the host's events for everything that came with it.</summary>
    public void HandOver(UserId to, NamedRole ownerRole, DateTimeOffset now)
    {
        RequireNotArchived();
        var named = Members.NameOwner(to, ownerRole, now);
        OwnerId = named.Owner;

        // What the new owner's membership lost on the way: the roles of one that had ended, or a role that never counted.
        foreach (var dropped in named.RolesDropped)
        {
            RaiseDomainEvent(new DocumentRoleTaken(Id, named.Owner, dropped.RoleId));
        }

        // Shared now, or shared again: a membership that had ended begins anew.
        if (named.Added || named.BeganAnew)
        {
            RaiseDomainEvent(new DocumentShared(Id, named.Owner));
        }

        if (named.RoleGiven)
        {
            RaiseDomainEvent(new DocumentRoleGiven(Id, named.Owner, ownerRole));
        }

        if (named.RoleTaken)
        {
            RaiseDomainEvent(new DocumentRoleTaken(Id, named.Previous, ownerRole));
        }

        RaiseDomainEvent(new DocumentHandedOver(Id, named.Previous, named.Owner));
    }

    /// <summary>Archives the document: nothing about it changes afterwards.</summary>
    public void MoveToArchive() => Archived = true;

    /// <summary>The host's guard, which runs in front of every call to the member list: the whole hook.</summary>
    private void RequireNotArchived()
    {
        if (Archived)
        {
            throw new RefusalException(DocumentRefusals.Archived, RefusalKind.Conflict, "The document is archived.");
        }
    }

    /// <summary>A user is on a document once: the member list's check, under the host's own code.</summary>
    public sealed class OneSharePerUser : IInvariant<Document>
    {
        /// <inheritdoc />
        public string Code => DocumentRefusals.Membership[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Document entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The owner stays, with a role that does not run out: the member list's check, under the host's own code.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Document>
    {
        /// <inheritdoc />
        public string Code => DocumentRefusals.Membership[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Document entity) => entity.Members.OwnerStays();
    }
}

/// <summary>A document was written.</summary>
public sealed record DocumentWritten(DocumentId Document, UserId Owner) : DomainEvent;

/// <summary>A document was shared with a user.</summary>
public sealed record DocumentShared(DocumentId Document, UserId With) : DomainEvent;

/// <summary>A document is no longer shared with a user.</summary>
public sealed record DocumentUnshared(DocumentId Document, UserId User) : DomainEvent;

/// <summary>A user was given a role on a document.</summary>
public sealed record DocumentRoleGiven(DocumentId Document, UserId To, NamedRole Role) : DomainEvent;

/// <summary>A role was taken from a user on a document.</summary>
public sealed record DocumentRoleTaken(DocumentId Document, UserId From, NamedRole Role) : DomainEvent;

/// <summary>A document was handed to another owner.</summary>
public sealed record DocumentHandedOver(DocumentId Document, UserId From, UserId To) : DomainEvent;
