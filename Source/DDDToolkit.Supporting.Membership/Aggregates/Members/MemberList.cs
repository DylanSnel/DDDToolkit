using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// The members of one resource, with the rules about them: who is a member for which period, which roles
/// each member holds for which period, and what the owner may not lose. The resource's aggregate holds one
/// over its own member collection, which the package's generator writes as <c>Members</c>. Its constructor opens
/// the list, and every other change goes through the member manager the generator writes for the member class:
/// <code>
/// [AggregateRoot&lt;DocumentId&gt;]
/// public sealed partial class Document
/// {
///     public Document(DocumentId id, UserId owner, DateTimeOffset now) : base(id)
///     {
///         OwnerId = owner;
///         Members.Open(now);
///     }
///
///     [Owner] public UserId OwnerId { get; private set; }
///
///     public partial IReadOnlyList&lt;DocumentShare&gt; Shares { get; }
///
///     partial void OnChangingMembers() =&gt; RequireNotArchived();
///     partial void OnMemberAdded(DocumentShare share) =&gt; RaiseDomainEvent(new DocumentShared(Id, share.MemberId));
/// }
/// </code>
/// <para>
/// The package's generator writes <c>Members</c> on the resource a member class names, from the one collection
/// of the member class, the property marked <c>[Owner]</c>, and the codes it writes on the resource, or the one
/// static <see cref="MembershipCodes"/> the resource declares itself. A new member row's id is made by the id
/// itself, with its <c>Create()</c>, which the generator writes for an <c>[EntityId&lt;Guid&gt;]</c> and an id over
/// anything else declares. A resource the generator cannot tell these of declares the property itself, which the
/// generator then leaves alone:
/// <code>
/// private MemberList&lt;DocumentShare, DocumentShareId, UserId, NamedRole&gt; Members
///     =&gt; new(_shares, OwnerId, DocumentRefusals.Membership);
/// </code>
/// </para>
/// <para>
/// It is not an aggregate and keeps nothing itself: the members live inside the resource's aggregate, so they
/// share its version, its save and whatever the aggregate refuses for its own reasons. The generator writes the
/// manager's way to it onto the resource as well (<see cref="IMembershipResource{TMember, TMemberId, TRoleId}"/>): each
/// change calls the aggregate's guard, <c>OnChangingMembers()</c>, a closed or archived resource say, then the list,
/// then the hook that says what happened, where the aggregate raises its own events under its own names.
/// </para>
/// <para>
/// Each method refuses what would break a rule with a coded <see cref="RefusalException"/>, under the
/// resource's own codes, before it changes anything, and answers what it did. It raises no events. The rules:
/// </para>
/// <list type="bullet">
/// <item>A member is on the list once, for a period. A membership that has ended is over for good: the member
/// comes back with a new one, without the roles of the old. One that still runs, or is still to start, is
/// the member's one membership: a second is refused, whenever it would start.</item>
/// <item>A member holds each role at most once, each for a period of its own. A role counts only while the
/// membership does, so none is given to a member whose membership has ended.</item>
/// <item>There is one owner, always a member, with no end, and holding no role: the owner holds what it holds by
/// owning the resource, the permissions marked <c>Owner</c>. The owner cannot be removed, nor be given a role,
/// until somebody else is named owner; the owner before then becomes what the call says, a member in a role or
/// nobody on the list (<see cref="FormerOwner"/>).</item>
/// </list>
/// <para>
/// Two of the rules are about the whole list, which only the aggregate has, so they are checks here
/// (<see cref="OneMembershipPerMember"/>, <see cref="OwnerStays"/>) for two invariants of the aggregate to
/// call, under the aggregate's own codes. The third, one hold per role, a member checks by itself.
/// </para>
/// <para>
/// Whether a member or a role may go on the list at all cannot be told from the list, and neither can who may change
/// it: the member manager asks both before it calls the list, the caller's permission that manages the members and
/// what the application answers about who may be a member and which roles go to one.
/// </para>
/// </summary>
/// <typeparam name="TMember">The application's member class.</typeparam>
/// <typeparam name="TId">The member class's own id, which makes a new member row's id with its <c>Create()</c>.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed class MemberList<TMember, TId, TMemberId, TRoleId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, ICreatableEntityId<TId>, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly List<TMember> _collection;
    private readonly TMemberId _owner;
    private readonly MembershipCodes _codes;

    /// <summary>The member list of one resource, over the collection its aggregate keeps.</summary>
    /// <param name="members">The aggregate's own member collection: the field behind its generated read-only property.</param>
    /// <param name="owner">The resource's owner, as the aggregate keeps it now.</param>
    /// <param name="codes">The codes the resource refuses with.</param>
    public MemberList(List<TMember> members, TMemberId owner, MembershipCodes codes)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(codes);

        _collection = members;
        _owner = owner;
        _codes = codes;
    }

    /// <summary>
    /// Opens the list of a new resource: puts its owner on it, from <paramref name="now"/> on and with no end,
    /// holding no role, since the owner holds what it holds by owning the resource. Called once, where the aggregate
    /// is constructed.
    /// </summary>
    /// <param name="now">When the resource is opened.</param>
    /// <returns>The owner's membership.</returns>
    /// <exception cref="InvalidOperationException">The list has members already: a resource is opened once.</exception>
    public TMember Open(DateTimeOffset now)
    {
        if (_collection.Count > 0)
        {
            throw new InvalidOperationException(
                "This member list has members already. Open puts the owner on the list of a new resource, once, where its aggregate is constructed.");
        }

        return NewMember(_owner, MemberPeriod.Open(now), addedBy: null);
    }

    /// <summary>
    /// Adds a member for <paramref name="period"/>, with no role yet: what else it may do comes with the roles
    /// it is given (<see cref="GiveRole"/>). A member is on the list once: a membership it still has at
    /// <paramref name="now"/>, one that runs or one that is still to start, is refused, whenever the new one
    /// would start. One that has ended by <paramref name="now"/> is replaced, with the roles that were held
    /// in it, whatever start the new one is given.
    /// </summary>
    /// <param name="member">The member; the member manager has checked it may be one.</param>
    /// <param name="period">When the membership counts.</param>
    /// <param name="now">
    /// When the member is added: what a membership it had before is judged by. Not the period's start, which
    /// may lie ahead: a membership that runs today is not over because another is to start next month.
    /// </param>
    /// <param name="by">Who adds it, or <see langword="null"/> for system work.</param>
    /// <returns>The new membership.</returns>
    /// <exception cref="RefusalException"><c>invalid-period</c>, <c>already-member</c>, under the resource's codes.</exception>
    public TMember Add(TMemberId member, MemberPeriod period, DateTimeOffset now, TMemberId? by = null)
    {
        RequireNotEmpty(period);
        if (Find(member) is { } before)
        {
            if (!before.EndedBy(now))
            {
                throw _codes.Refuse(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, member));
            }

            // Its time ran out, so this is a new membership and not the old one going on: nothing the member
            // held before comes back with it. An owner's membership has no end, so this is never the owner's.
            _collection.Remove(before);
        }

        return NewMember(member, period, by);
    }

    /// <summary>
    /// Adds a member for <paramref name="period"/> and gives it <paramref name="role"/> for the same period, in
    /// one change.
    /// </summary>
    /// <param name="member">The member; the member manager has checked it may be one.</param>
    /// <param name="role">The role; the member manager has checked it may go to a member.</param>
    /// <param name="period">When the membership, and the role, count.</param>
    /// <param name="now">When the member is added: what a membership it had before is judged by.</param>
    /// <param name="by">Who adds it, or <see langword="null"/> for system work.</param>
    /// <returns>The new membership, holding the role.</returns>
    /// <exception cref="RefusalException"><c>invalid-period</c>, <c>already-member</c>, under the resource's codes.</exception>
    public TMember Add(TMemberId member, TRoleId role, MemberPeriod period, DateTimeOffset now, TMemberId? by = null)
    {
        var added = Add(member, period, now, by);
        added.Give(role, period, by);
        return added;
    }

    /// <summary>
    /// Gives a member a role for <paramref name="period"/>, next to whatever roles it holds already. A member
    /// holds a role once: a hold it still has at <paramref name="now"/>, one that runs or one that is still to
    /// start, is refused, whenever the new one would start, and one that has ended by <paramref name="now"/>
    /// is replaced. A member whose membership has ended by <paramref name="now"/> is not a member for this: a
    /// role counts only while the membership does, so giving one would give nothing. Add the member again
    /// first.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="role">The role; the member manager has checked it may go to a member.</param>
    /// <param name="period">When the role counts. It counts only while the membership does.</param>
    /// <param name="now">
    /// When the role is given: what the membership, and a hold of the role from before, are judged by. Not
    /// the period's start, which may lie ahead: a role held today is not over because it is given again from
    /// next month.
    /// </param>
    /// <param name="by">Who gives it, or <see langword="null"/> for system work.</param>
    /// <returns>The role as the member holds it now.</returns>
    /// <exception cref="RefusalException">
    /// <c>invalid-period</c>, <c>member-not-found</c>, <c>role-held</c>, and <c>owner-protected</c> for the owner, who
    /// holds no role, under the resource's codes.
    /// </exception>
    public MemberRole<TMemberId, TRoleId> GiveRole(TMemberId member, TRoleId role, MemberPeriod period, DateTimeOffset now, TMemberId? by = null)
    {
        RequireNotEmpty(period);
        if (member.Equals(_owner))
        {
            // The owner holds what it holds by owning; a role would give it more, or nothing.
            throw _codes.Refuse(MembershipRefusals.OwnerProtected);
        }

        var found = Find(member) is { } on && !on.EndedBy(now)
            ? on
            : throw _codes.Refuse(MembershipRefusals.MemberNotFound, (MembershipCodes.DefaultMemberArgument, member));
        if (found.Find(role) is { } held && (held.EndsAt is null || held.EndsAt > now))
        {
            throw _codes.Refuse(MembershipRefusals.RoleHeld, (MembershipCodes.DefaultMemberArgument, member), ("Role", role));
        }

        return found.Give(role, period, by);
    }

    /// <summary>
    /// Takes a role from a member, who stays a member with whatever else it holds. Any role can be taken, one
    /// that is no longer in use too: what was given once can always be taken back.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="role">The role to take.</param>
    /// <returns>The role as the member held it.</returns>
    /// <exception cref="RefusalException"><c>member-not-found</c>, <c>role-not-held</c>, under the resource's codes.</exception>
    public MemberRole<TMemberId, TRoleId> TakeRole(TMemberId member, TRoleId role)
    {
        var found = Find(member) ?? throw _codes.Refuse(MembershipRefusals.MemberNotFound, (MembershipCodes.DefaultMemberArgument, member));
        if (found.Find(role) is null)
        {
            throw _codes.Refuse(MembershipRefusals.RoleNotHeld, (MembershipCodes.DefaultMemberArgument, member), ("Role", role));
        }

        return found.Take(role)!;
    }

    /// <summary>Takes a member off the list, with every role it holds. The owner stays until somebody else is named owner.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The membership as it was, with the roles held in it.</returns>
    /// <exception cref="RefusalException"><c>member-not-found</c>, <c>owner-protected</c>, under the resource's codes.</exception>
    public TMember Remove(TMemberId member)
    {
        var found = Find(member) ?? throw _codes.Refuse(MembershipRefusals.MemberNotFound, (MembershipCodes.DefaultMemberArgument, member));
        if (member.Equals(_owner))
        {
            throw _codes.Refuse(MembershipRefusals.OwnerProtected);
        }

        _collection.Remove(found);
        return found;
    }

    /// <summary>
    /// Names another owner: a member from now on and with no end, added when not a member yet, holding no role. The
    /// roles it held go, since the owner holds what it holds by owning; a membership that had ended begins anew, and
    /// one that was still to start or to end loses that start or end. The owner before becomes what
    /// <paramref name="formerOwner"/> says: a member with no end in the role it names, or nobody on the list. There is
    /// no default, since the owner held no role to fall back to.
    /// <para>
    /// The list does not keep who the owner is: the aggregate does, and keeps the answer's
    /// <see cref="OwnerNamed{TMemberId, TRoleId}.Owner"/> as its owner right after this call. The answer says
    /// everything that changed, so the aggregate can raise an event for each change, and can keep the end that was
    /// lifted (<see cref="OwnerNamed{TMemberId, TRoleId}.EndLifted"/>) if somebody who was on the resource for a
    /// while is to leave it at that moment after all: nothing here ends a membership again, short of
    /// <see cref="Remove"/>.
    /// </para>
    /// </summary>
    /// <param name="member">The member that is to own the resource; the member manager has checked it may be one.</param>
    /// <param name="formerOwner">
    /// What the owner before becomes: <see cref="FormerOwner.StaysAs{TRoleId}"/> a role, which the member manager has checked
    /// may go to a member, or <see cref="FormerOwner.Leaves"/>.
    /// </param>
    /// <param name="now">When the owner is named.</param>
    /// <returns>What happened, for the aggregate to keep its owner by and raise its events from.</returns>
    /// <exception cref="RefusalException"><c>already-owner</c>, under the resource's codes.</exception>
    public OwnerNamed<TMemberId, TRoleId> NameOwner(TMemberId member, FormerOwner<TRoleId> formerOwner, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(formerOwner);
        if (member.Equals(_owner))
        {
            throw _codes.Refuse(MembershipRefusals.AlreadyOwner);
        }

        var named = Find(member);
        var added = named is null;
        named ??= NewMember(member, MemberPeriod.Open(now), addedBy: null);
        var place = named.StayAsOwner(now);

        var previous = Find(_owner);
        if (formerOwner.Role is { } role)
        {
            // The owner held no role and no end; it stays on, as the member it now is.
            previous ??= NewMember(_owner, MemberPeriod.Open(now), addedBy: null);
            previous.Give(role, MemberPeriod.Open(now), givenBy: null);
        }
        else if (previous is not null)
        {
            _collection.Remove(previous);
        }

        return new OwnerNamed<TMemberId, TRoleId>(
            _owner, member, added, place.BeganAnew, place.RolesDropped, place.EndLifted, place.StartMoved, formerOwner.Role, formerOwner.Role is null);
    }

    /// <summary>
    /// The rule that a member is on the list once, checked after the fact: for an invariant of the aggregate to
    /// call, under the aggregate's own code for <see cref="MembershipRefusals.AlreadyMember"/>.
    /// <code>
    /// public sealed class OneShareePerDocument : IInvariant&lt;Document&gt;
    /// {
    ///     public string Code =&gt; DocumentRefusals.Membership[MembershipRefusals.AlreadyMember];
    ///
    ///     public InvariantFailure? Check(Document entity) =&gt; entity.Members.OneMembershipPerMember();
    /// }
    /// </code>
    /// </summary>
    /// <returns><see langword="null"/> when the rule holds; otherwise what is wrong, with the member that is there twice.</returns>
    public InvariantFailure? OneMembershipPerMember()
        => _collection.GroupBy(member => member.MemberId).FirstOrDefault(member => member.Count() > 1) is { } twice
            ? _codes.FailureOf(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, twice.Key))
            : null;

    /// <summary>
    /// The rule that the owner is a member with no end, holding no role, checked after the fact: for an invariant of
    /// the aggregate to call, under the aggregate's own code for <see cref="MembershipRefusals.OwnerProtected"/>.
    /// </summary>
    /// <returns><see langword="null"/> when the rule holds; otherwise what is wrong.</returns>
    public InvariantFailure? OwnerStays()
        => _collection.Any(member => member.MemberId.Equals(_owner) && member.EndsAt is null && member.Roles.Count == 0)
            ? null
            : _codes.FailureOf(MembershipRefusals.OwnerProtected);

    private TMember? Find(TMemberId member) => _collection.FirstOrDefault(candidate => candidate.MemberId.Equals(member));

    private TMember NewMember(TMemberId member, MemberPeriod period, TMemberId? addedBy)
    {
        var created = HostInstances<TMember>.New();
        created.InitializeNew(TId.Create(), member, period, addedBy);
        _collection.Add(created);
        return created;
    }

    /// <summary>Refuses a period that covers no moment, before anything changes.</summary>
    private void RequireNotEmpty(MemberPeriod period)
    {
        if (period.IsEmpty)
        {
            throw _codes.Refuse(MembershipRefusals.InvalidPeriod);
        }
    }
}
