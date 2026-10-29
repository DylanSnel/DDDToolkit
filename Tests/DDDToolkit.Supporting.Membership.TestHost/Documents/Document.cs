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
/// the host's own, declared as it always was. It keeps its owner and its shares, and writes its guard and its events
/// in the hooks the generator declares, which the member manager, <c>DocumentShareManager</c>, calls around each change.
/// </summary>
[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    /// <summary>Writes a document, with <paramref name="owner"/> a member from <paramref name="now"/> on, for good, holding no role: it holds what an owner does by owning it.</summary>
    public Document(DocumentId id, string title, UserId owner, DateTimeOffset now) : base(id)
    {
        Title = title;
        OwnerId = owner;
        Members.Open(now);
        RaiseDomainEvent(new DocumentWritten(id, owner));
    }

    /// <summary>The document's title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Whether the document is archived: an archived document changes no more.</summary>
    public bool Archived { get; private set; }

    /// <summary>The user that owns the document.</summary>
    /// <remarks>
    /// Also where the document hangs, <c>[At]</c>: a suite that lets a user's deputy reach what that user owns reaches a
    /// document from its owner. Rules that do not reach from above read nothing of it.
    /// </remarks>
    [Owner]
    [At]
    public UserId OwnerId { get; private set; }

    /// <summary>Who the document is shared with: the collection the member list works on.</summary>
    public partial IReadOnlyList<DocumentShare> Shares { get; }

    /// <summary>
    /// The codes the rules about a document's shares refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => DocumentRefusals.Membership;

    /// <summary>
    /// The way to the members the member manager takes, which the generator writes: the methods below, which the tests
    /// set up their documents with, change the members through it as the manager does, so the hooks raise the events.
    /// </summary>
    private IMembershipResource<DocumentShare, UserId, NamedRole> Managed => this;

    /// <summary>Shares the document with a user, with no role yet.</summary>
    public DocumentShare ShareWith(UserId with, MemberPeriod period, DateTimeOffset now, UserId? by) => Managed.AddMember(with, null, period, now, by);

    /// <summary>Shares the document with a user in a role, both for the same period.</summary>
    public DocumentShare ShareWith(UserId with, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by) => Managed.AddMember(with, role, period, now, by);

    /// <summary>Gives a user the document is shared with a role.</summary>
    public MemberRole<UserId, NamedRole> GiveRole(UserId to, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by) => Managed.GiveRole(to, role, period, now, by);

    /// <summary>Takes a role from a user the document is shared with.</summary>
    public void TakeRole(UserId from, NamedRole role) => Managed.TakeRole(from, role);

    /// <summary>Stops sharing the document with a user.</summary>
    public void Unshare(UserId user) => Managed.RemoveMember(user);

    /// <summary>Hands the document to another owner.</summary>
    public void HandOver(UserId to, FormerOwner<NamedRole> formerOwner, DateTimeOffset now) => Managed.NameOwner(to, formerOwner, now);

    /// <summary>The host's guard, which runs in front of every change of the members: the whole hook.</summary>
    partial void OnChangingMembers() => RequireNotArchived();

    partial void OnMemberAdded(DocumentShare member) => RaiseDomainEvent(new DocumentShared(Id, member.MemberId));

    partial void OnMemberRemoved(UserId member) => RaiseDomainEvent(new DocumentUnshared(Id, member));

    partial void OnRoleGiven(UserId member, MemberRole<UserId, NamedRole> role) => RaiseDomainEvent(new DocumentRoleGiven(Id, member, role.RoleId));

    partial void OnRoleTaken(UserId member, MemberRole<UserId, NamedRole> role) => RaiseDomainEvent(new DocumentRoleTaken(Id, member, role.RoleId));

    /// <summary>The host's events for everything that came with a hand-over.</summary>
    partial void OnOwnerNamed(OwnerNamed<UserId, NamedRole> named)
    {
        // What the new owner held on the list: it holds by owning now, so its roles go.
        foreach (var dropped in named.RolesDropped)
        {
            RaiseDomainEvent(new DocumentRoleTaken(Id, named.Owner, dropped.RoleId));
        }

        // Shared now, or shared again: a membership that had ended begins anew.
        if (named.Added || named.BeganAnew)
        {
            RaiseDomainEvent(new DocumentShared(Id, named.Owner));
        }

        // The old owner stays in a role, or leaves.
        if (named.PreviousRole is { } role)
        {
            RaiseDomainEvent(new DocumentRoleGiven(Id, named.Previous, role));
        }
        else if (named.PreviousLeft)
        {
            RaiseDomainEvent(new DocumentUnshared(Id, named.Previous));
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

    /// <summary>The owner stays, for good and holding no role: the member list's check, under the host's own code.</summary>
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
