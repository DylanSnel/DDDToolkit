using System.ComponentModel;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// The member manager's way to a resource's members, which the package's generator writes onto the resource beside its
/// member list, explicitly, so none of it shows on the resource: an application never implements it and never calls it.
/// <code>
/// // Written onto Board for [Member&lt;BoardMemberId, SeatId, BoardRoleId, Board&gt;]:
/// partial void OnChangingMembers();
/// partial void OnMemberAdded(BoardMember member);
/// // ... OnMemberRemoved, OnRoleGiven, OnRoleTaken, OnOwnerNamed
///
/// BoardMember IMembershipResource&lt;BoardMember, SeatId, BoardRoleId&gt;.AddMember(...)
/// {
///     OnChangingMembers();
///     var added = Members.Add(...);
///     OnMemberAdded(added);
///     return added;
/// }
/// </code>
/// Each change calls the resource's guard, <c>OnChangingMembers()</c>, then the member list, then the hook that says
/// what happened; naming an owner also sets the property marked <c>[Owner]</c>. The hooks are partial methods the
/// application writes where it has a guard or an event to raise, and leaves out where it has none.
/// </summary>
/// <typeparam name="TMember">The application's member class.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMembershipResource<TMember, TMemberId, TRoleId>
    where TMember : class
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The manager classes the generator wrote for the resource, which its registration registers.</summary>
    static abstract IReadOnlyList<Type> MembershipManagers { get; }

    /// <summary>The resource's owner: the property marked <c>[Owner]</c>.</summary>
    TMemberId Owner { get; }

    /// <summary>The resource's members, as its own collection holds them.</summary>
    IReadOnlyList<TMember> Members { get; }

    /// <summary>Adds a member, with <paramref name="role"/> for the same period where one is given.</summary>
    /// <param name="member">The member.</param>
    /// <param name="role">The role it holds from the start, or <see langword="null"/> for none.</param>
    /// <param name="period">When the membership counts.</param>
    /// <param name="now">When it is added.</param>
    /// <param name="by">Who adds it, or <see langword="null"/> for system work.</param>
    TMember AddMember(TMemberId member, TRoleId? role, MemberPeriod period, DateTimeOffset now, TMemberId? by);

    /// <summary>Gives a member a role.</summary>
    /// <param name="member">The member.</param>
    /// <param name="role">The role.</param>
    /// <param name="period">When the role counts.</param>
    /// <param name="now">When it is given.</param>
    /// <param name="by">Who gives it, or <see langword="null"/> for system work.</param>
    MemberRole<TMemberId, TRoleId> GiveRole(TMemberId member, TRoleId role, MemberPeriod period, DateTimeOffset now, TMemberId? by);

    /// <summary>Takes a role from a member.</summary>
    /// <param name="member">The member.</param>
    /// <param name="role">The role.</param>
    MemberRole<TMemberId, TRoleId> TakeRole(TMemberId member, TRoleId role);

    /// <summary>Takes a member off the list.</summary>
    /// <param name="member">The member.</param>
    TMember RemoveMember(TMemberId member);

    /// <summary>Names another owner, and keeps it in the property marked <c>[Owner]</c>.</summary>
    /// <param name="member">The member that is to own the resource.</param>
    /// <param name="formerOwner">What the owner before becomes.</param>
    /// <param name="now">When the owner is named.</param>
    OwnerNamed<TMemberId, TRoleId> NameOwner(TMemberId member, FormerOwner<TRoleId> formerOwner, DateTimeOffset now);
}
