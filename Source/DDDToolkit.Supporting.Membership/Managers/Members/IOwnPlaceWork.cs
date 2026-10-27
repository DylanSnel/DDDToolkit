using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// Begins the resource's own work for the save of a change to the caller's own place on it: a role it takes from
/// itself, itself taken off, leaving, or handing on the resource it owns. Such a change can take away the very right the
/// save needs where a database checks every row, so the member manager saves it as the resource's own work in its
/// scope, for the request in hand whose check read the resource, when the unit of work holds nothing else.
/// <para>
/// Optional. Without one the manager begins system work in that scope for the current user,
/// <c>user.BeginSystem(scope)</c>. The join with an organization's package registers one that begins it in the caller's
/// organization, for the caller's seat.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
public interface IOwnPlaceWork<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
{
    /// <summary>Begins the resource's own work in <paramref name="scope"/>, for <paramref name="member"/>, the caller.</summary>
    /// <param name="member">The caller, as a member of the resource.</param>
    /// <param name="scope">
    /// The scope of the resource's own work, one its rules name; <see langword="null"/> for rules that name none, whose
    /// own work is the application's without a scope.
    /// </param>
    /// <returns>What ends the work when disposed.</returns>
    IDisposable Begin(TMemberId member, string? scope);
}
