using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// The members of one resource, with the rules about them: who is a member for which period, which roles
/// each member holds for which period, and what the owner may not lose. The resource's aggregate holds one
/// over its own member collection, and its methods that change members go through it:
/// <code>
/// [AggregateRoot&lt;DocumentId&gt;]
/// public sealed partial class Document
/// {
///     public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");
///
///     public UserId OwnerId { get; private set; }
///
///     public partial IReadOnlyList&lt;DocumentShare&gt; Shares { get; }
///
///     public void ShareWith(UserId user, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by)
///     {
///         RequireNotArchived();
///         Members.Add(user, role, period, now, by);
///     }
/// }
/// </code>
/// <para>
/// The package's generator writes <c>Members</c> on the resource a member class names, from the one collection
/// of the member class, the one property of the member's id, which is the owner, and the one static
/// <see cref="MembershipCodes"/> the resource declares; a new member row's id is made with the member class's
/// <c>CreateSequential</c>, so that id is an <c>[EntityId&lt;Guid&gt;]</c>. A resource the generator cannot tell
/// these of declares the property itself, which the generator then leaves alone:
/// <code>
/// private MemberList&lt;DocumentShare, DocumentShareId, UserId, NamedRole&gt; Members
///     =&gt; new(_shares, OwnerId, DocumentShareId.CreateSequential, DocumentRefusals.Membership);
/// </code>
/// </para>
/// <para>
/// It is not an aggregate and keeps nothing itself: the members live inside the resource's aggregate, so they
/// share its version, its save and whatever the aggregate refuses for its own reasons. The aggregate's own
/// guard, a closed or archived resource say, runs in front of each call here, and that is the whole hook: a
/// resource that changes no more simply does not call.
/// </para>
/// <para>
/// Each method refuses what would break a rule with a coded <see cref="RefusalException"/>, under the
/// resource's own codes, before it changes anything, and answers what it did. It raises no events: the
/// aggregate raises its own from the answer, under its own names. The rules:
/// </para>
/// <list type="bullet">
/// <item>A member is on the list once, for a period. A membership that has ended is over for good: the member
/// comes back with a new one, without the roles of the old. One that still runs, or is still to start, is
/// the member's one membership: a second is refused, whenever it would start.</item>
/// <item>A member holds each role at most once, each for a period of its own. A role counts only while the
/// membership does, so none is given to a member whose membership has ended.</item>
/// <item>There is one owner, always a member, with no end, holding the owner's role with no end either. The
/// owner cannot be removed, nor lose that role, until somebody else is named owner; the owner before then
/// loses the owner's role, and that role alone, and stays a member with no end.</item>
/// </list>
/// <para>
/// Two of the rules are about the whole list, which only the aggregate has, so they are checks here
/// (<see cref="OneMembershipPerMember"/>, <see cref="OwnerStays"/>) for two invariants of the aggregate to
/// call, under the aggregate's own codes. The third, one hold per role, a member checks by itself.
/// </para>
/// <para>
/// Which role is the owner's, and whether a member or a role may go on the list at all, cannot be told from
/// the list: the use case asks (<see cref="UseCases.MemberAdmission{TResourceId, TMemberId, TRoleId}"/>) and
/// passes the answer in. Neither can who may change the list, which no part of the package decides: the
/// application's command requires what its caller needs before any of these methods is reached.
/// </para>
/// </summary>
/// <typeparam name="TMember">The application's member class.</typeparam>
/// <typeparam name="TId">The member class's own id.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed class MemberList<TMember, TId, TMemberId, TRoleId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly List<TMember> _collection;
    private readonly TMemberId _owner;
    private readonly Func<TId> _newId;
    private readonly MembershipCodes _codes;

    /// <summary>The member list of one resource, over the collection its aggregate keeps.</summary>
    /// <param name="members">The aggregate's own member collection: the field behind its generated read-only property.</param>
    /// <param name="owner">The resource's owner, as the aggregate keeps it now.</param>
    /// <param name="newId">Makes the id of a new member row, such as <c>DocumentShareId.CreateSequential</c>.</param>
    /// <param name="codes">The codes the resource refuses with.</param>
    public MemberList(List<TMember> members, TMemberId owner, Func<TId> newId, MembershipCodes codes)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(newId);
        ArgumentNullException.ThrowIfNull(codes);

        _collection = members;
        _owner = owner;
        _newId = newId;
        _codes = codes;
    }

    /// <summary>
    /// Opens the list of a new resource: puts its owner on it, holding <paramref name="ownerRole"/>, both from
    /// <paramref name="now"/> on and with no end. Called once, where the aggregate is constructed.
    /// </summary>
    /// <param name="ownerRole">The owner's role, which the use case found.</param>
    /// <param name="now">When the resource is opened.</param>
    /// <returns>The owner's membership.</returns>
    /// <exception cref="InvalidOperationException">The list has members already: a resource is opened once.</exception>
    public TMember Open(TRoleId ownerRole, DateTimeOffset now)
    {
        if (_collection.Count > 0)
        {
            throw new InvalidOperationException(
                "This member list has members already. Open puts the owner on the list of a new resource, once, where its aggregate is constructed.");
        }

        var member = NewMember(_owner, MemberPeriod.Open(now), addedBy: null);
        member.StayAsOwner(ownerRole, now);
        return member;
    }

    /// <summary>
    /// Adds a member for <paramref name="period"/>, with no role yet: what else it may do comes with the roles
    /// it is given (<see cref="GiveRole"/>). A member is on the list once: a membership it still has at
    /// <paramref name="now"/>, one that runs or one that is still to start, is refused, whenever the new one
    /// would start. One that has ended by <paramref name="now"/> is replaced, with the roles that were held
    /// in it, whatever start the new one is given.
    /// </summary>
    /// <param name="member">The member; the use case has checked it may be one.</param>
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
                throw _codes.Of(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, member));
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
    /// <param name="member">The member; the use case has checked it may be one.</param>
    /// <param name="role">The role; the use case has checked it may go to a member.</param>
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
    /// <param name="role">The role; the use case has checked it may go to a member.</param>
    /// <param name="period">When the role counts. It counts only while the membership does.</param>
    /// <param name="now">
    /// When the role is given: what the membership, and a hold of the role from before, are judged by. Not
    /// the period's start, which may lie ahead: a role held today is not over because it is given again from
    /// next month.
    /// </param>
    /// <param name="by">Who gives it, or <see langword="null"/> for system work.</param>
    /// <returns>The role as the member holds it now.</returns>
    /// <exception cref="RefusalException"><c>invalid-period</c>, <c>member-not-found</c>, <c>role-held</c>, under the resource's codes.</exception>
    public MemberRole<TMemberId, TRoleId> GiveRole(TMemberId member, TRoleId role, MemberPeriod period, DateTimeOffset now, TMemberId? by = null)
    {
        RequireNotEmpty(period);
        var found = Find(member) is { } on && !on.EndedBy(now)
            ? on
            : throw _codes.Of(MembershipRefusals.MemberNotFound, (MembershipCodes.DefaultMemberArgument, member));
        if (found.Find(role) is { } held && (held.EndsAt is null || held.EndsAt > now))
        {
            throw _codes.Of(MembershipRefusals.RoleHeld, (MembershipCodes.DefaultMemberArgument, member), ("Role", role));
        }

        return found.Give(role, period, by);
    }

    /// <summary>
    /// Takes a role from a member, who stays a member with whatever else it holds. Any role can be taken, one
    /// that is no longer in use too: what was given once can always be taken back. The owner keeps the owner's
    /// role, and never ends up without any role that has no end, until somebody else is named owner.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="role">The role to take.</param>
    /// <param name="ownerRole">
    /// The owner's role, as the use case found it, or <see langword="null"/> when there is none in use. Read
    /// for the owner only, so a use case asks for it only when <paramref name="member"/> is the owner.
    /// </param>
    /// <returns>The role as the member held it.</returns>
    /// <exception cref="RefusalException"><c>member-not-found</c>, <c>role-not-held</c>, <c>owner-protected</c>, under the resource's codes.</exception>
    public MemberRole<TMemberId, TRoleId> TakeRole(TMemberId member, TRoleId role, TRoleId? ownerRole)
    {
        var found = Find(member) ?? throw _codes.Of(MembershipRefusals.MemberNotFound, (MembershipCodes.DefaultMemberArgument, member));
        if (found.Find(role) is null)
        {
            throw _codes.Of(MembershipRefusals.RoleNotHeld, (MembershipCodes.DefaultMemberArgument, member), ("Role", role));
        }

        if (member.Equals(_owner)
            && ((ownerRole is { } owners && role.Equals(owners)) || !found.Roles.Any(other => !other.RoleId.Equals(role) && other.EndsAt is null)))
        {
            throw _codes.Of(MembershipRefusals.OwnerProtected);
        }

        return found.Take(role)!;
    }

    /// <summary>Takes a member off the list, with every role it holds. The owner stays until somebody else is named owner.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The membership as it was, with the roles held in it.</returns>
    /// <exception cref="RefusalException"><c>member-not-found</c>, <c>owner-protected</c>, under the resource's codes.</exception>
    public TMember Remove(TMemberId member)
    {
        var found = Find(member) ?? throw _codes.Of(MembershipRefusals.MemberNotFound, (MembershipCodes.DefaultMemberArgument, member));
        if (member.Equals(_owner))
        {
            throw _codes.Of(MembershipRefusals.OwnerProtected);
        }

        _collection.Remove(found);
        return found;
    }

    /// <summary>
    /// Names another owner: a member from now on and with no end, added when not a member yet, holding
    /// <paramref name="ownerRole"/> with no end. A member whose membership had ended begins anew, as the owner
    /// and nothing else: the roles of the ended membership do not come back. A member whose membership was
    /// still to end loses that end, and the roles held in it stop where it would have: a role with no end of
    /// its own gets the end the membership had. One whose membership was still to start is a member from now
    /// on, and its roles count no sooner than they were to. The owner before loses the owner's role and
    /// nothing else: it stays a member, with no end, with every other role it holds.
    /// <para>
    /// The list does not keep who the owner is: the aggregate does, and keeps the answer's
    /// <see cref="OwnerNamed{TMemberId, TRoleId}.Owner"/> as its owner right after this call. The answer says
    /// everything that changed about the new owner's membership, so the aggregate can raise an event for each
    /// change, and can keep the end that was lifted (<see cref="OwnerNamed{TMemberId, TRoleId}.EndLifted"/>)
    /// if somebody who was on the resource for a while is to leave it at that moment after all: nothing here
    /// ends a membership again, short of <see cref="Remove"/>.
    /// </para>
    /// </summary>
    /// <param name="member">The member that is to own the resource; the use case has checked it may be one.</param>
    /// <param name="ownerRole">The owner's role, as the use case found it.</param>
    /// <param name="now">When the owner is named.</param>
    /// <returns>What happened, for the aggregate to keep its owner by and raise its events from.</returns>
    /// <exception cref="RefusalException"><c>already-owner</c>, under the resource's codes.</exception>
    public OwnerNamed<TMemberId, TRoleId> NameOwner(TMemberId member, TRoleId ownerRole, DateTimeOffset now)
    {
        if (member.Equals(_owner))
        {
            throw _codes.Of(MembershipRefusals.AlreadyOwner);
        }

        var named = Find(member);
        var added = named is null;
        named ??= NewMember(member, MemberPeriod.Open(now), addedBy: null);

        var place = named.StayAsOwner(ownerRole, now);
        var taken = Find(_owner)?.Take(ownerRole) is not null;

        return new OwnerNamed<TMemberId, TRoleId>(_owner, member, added, place.RoleGiven, taken, place.BeganAnew, place.RolesDropped, place.EndLifted, place.StartMoved);
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
    /// The rule that the owner is a member with no end, and holds a role with no end, checked after the fact:
    /// for an invariant of the aggregate to call, under the aggregate's own code for
    /// <see cref="MembershipRefusals.OwnerProtected"/>. Which role is the owner's cannot be told from the list,
    /// so the use cases see to that; what can be told is that the owner's place and a role of theirs do not run
    /// out.
    /// </summary>
    /// <returns><see langword="null"/> when the rule holds; otherwise what is wrong.</returns>
    public InvariantFailure? OwnerStays()
        => _collection.Any(member => member.MemberId.Equals(_owner) && member.EndsAt is null && member.Roles.Any(held => held.EndsAt is null))
            ? null
            : _codes.FailureOf(MembershipRefusals.OwnerProtected);

    private TMember? Find(TMemberId member) => _collection.FirstOrDefault(candidate => candidate.MemberId.Equals(member));

    private TMember NewMember(TMemberId member, MemberPeriod period, TMemberId? addedBy)
    {
        var created = HostInstances<TMember>.New();
        created.InitializeNew(_newId(), member, period, addedBy);
        _collection.Add(created);
        return created;
    }

    /// <summary>Refuses a period that covers no moment, before anything changes.</summary>
    private void RequireNotEmpty(MemberPeriod period)
    {
        if (period.IsEmpty)
        {
            throw _codes.Of(MembershipRefusals.InvalidPeriod);
        }
    }
}
