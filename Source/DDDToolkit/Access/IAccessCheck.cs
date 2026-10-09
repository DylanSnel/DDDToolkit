namespace DDDToolkit.Access;

/// <summary>
/// Decides the requirements of one owner: a package ships one for the cases it declares, and a module writes
/// one for its own. Registered for a module's request interface with
/// <see cref="AccessCheckServiceCollectionExtensions.AddAccessCheck{TRequests, TCheck}"/>, and asked by
/// <see cref="AccessChecks{TRequests}"/> before a handler runs.
/// </summary>
/// <remarks>
/// A check is asked in two steps, so that a case that is not this check's can never be mistaken for a caller
/// it refuses: <see cref="Decides"/> says whether the requirement is one of its cases, which only looks at
/// the requirement, and <see cref="RequireAsync"/> then holds the caller to it, which may read.
/// <para>
/// Passing is returning; refusing is throwing, with the code of whoever the request belongs to
/// (<see cref="Exceptions.RefusalException"/>). So a branch a check forgets lets the caller through: a
/// <c>switch</c> over the cases <see cref="Decides"/> accepts ends in <c>default: throw</c>, and a case added
/// later without its branch refuses rather than passes.
/// </para>
/// <para>
/// What the check read on the way, and the handler needs, it keeps for the request
/// (<see cref="Checked{T}"/>), so the handler acts on what was checked.
/// </para>
/// </remarks>
public interface IAccessCheck
{
    /// <summary>
    /// Whether <paramref name="requirement"/> is one of this check's cases. It reads nothing and refuses
    /// nobody: it is asked for every requirement of the requests the check is registered for, and by a test
    /// that holds every requirement a module declares to having a check.
    /// </summary>
    /// <param name="requirement">What a request declared.</param>
    bool Decides(AccessRequirement requirement);

    /// <summary>
    /// Holds the caller to <paramref name="requirement"/>: returns when the caller meets it, and throws when it
    /// does not. Called only for a requirement <see cref="Decides"/> said yes to.
    /// </summary>
    /// <param name="requirement">What the request declared, read from it once.</param>
    /// <param name="request">The very request being handled, which is what anything kept for its handler is kept under.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <exception cref="Exceptions.RefusalException">The caller is not who the request requires.</exception>
    ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken);
}
