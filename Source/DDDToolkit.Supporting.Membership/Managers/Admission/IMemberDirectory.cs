using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// Says whether somebody can be made a member of one kind of resource: whether the id is one the application
/// knows, and whoever it stands for is active. An application implements it where it keeps its users, and the member
/// manager of that kind of resource asks it before a member is added, an owner named or a resource opened.
/// <para>
/// Optional. Without one every id is admitted: a resource whose members are users of an identity provider
/// often has nowhere to ask. The join with an organization's package answers it for members that are seats.
/// </para>
/// <para>
/// It is asked for by the resource, so two kinds of resource whose members are known by the same id each have
/// their own, or share one registered for both.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
public interface IMemberDirectory<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
{
    /// <summary>Whether <paramref name="member"/> is known and active, so that it may be made a member.</summary>
    /// <param name="member">The one to be made a member.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<bool> IsActiveAsync(TMemberId member, CancellationToken cancellationToken);
}
