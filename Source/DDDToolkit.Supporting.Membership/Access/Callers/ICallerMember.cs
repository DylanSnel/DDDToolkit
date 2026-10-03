using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Who the caller is as a member of one kind of resource, where the application resolves that itself
/// (<see cref="MemberSource.Resolved"/>): the place somebody has in an organization, say, which no token
/// carries. An application implements it where it knows its callers, and registers it with the resource.
/// <para>
/// It is asked once for every question, about a caller that signed in, and its answer is compared with the
/// members of a resource inside one statement. So it answers from what the application knows of the caller
/// already, without reading anything: an application that has to look its caller up does so once, where the
/// request begins, and answers from that here.
/// </para>
/// <para>
/// It is asked for by the resource, so two kinds of resource whose members are known by the same id each
/// have their own, or share one class registered for both.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
public interface ICallerMember<TResourceId, TMemberId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
{
    /// <summary>
    /// The member id <paramref name="caller"/> has, or <see langword="null"/> when it has none that counts
    /// now: somebody the application does not know, or knows and no longer counts. Such a caller is nobody's
    /// member, and holds only what reaches a resource from above. It refuses nobody.
    /// </summary>
    /// <param name="caller">The caller a question is asked for: a signed-in user.</param>
    TMemberId? Find(Caller caller);

    /// <summary>
    /// Refuses a caller the questions cannot be asked about at all, with the application's own refusal:
    /// somebody who is nobody in the organization the resources belong to. It is asked before anything is
    /// read, by the questions that refuse, about every caller that is not the application's own work. It
    /// returns for everybody unless the application says otherwise: a caller that is nobody's member then
    /// simply reaches nothing.
    /// </summary>
    /// <param name="caller">The caller a question is asked for.</param>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody the questions can be asked about.</exception>
    void Require(Caller caller)
    {
    }
}
