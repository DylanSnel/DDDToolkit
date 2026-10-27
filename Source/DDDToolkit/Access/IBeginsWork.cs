namespace DDDToolkit.Access;

/// <summary>
/// An access check that begins work around the handler of a request it let through. Tenancy's check runs a command an
/// operator sends as operator work, in the tenant the command names. <see cref="AccessChecks{TRequests}.BeginWork"/>
/// asks it after the checks let the request through, and the handler runs inside what it begins.
/// </summary>
/// <remarks>
/// A check implements it beside <see cref="IAccessCheck"/>. It is asked about each case of the request's requirement it
/// decides, in the order the cases are written. It begins nothing for a caller its case would not let through, so a case
/// of <see cref="AccessRequirement.Any"/> that the caller did not pass begins nothing.
/// <para>
/// What it begins is set in the flow of work that runs the handler, the way <see cref="Abstractions.Access.ICurrentUser.BeginSystem"/>
/// sets the current user.
/// It is set synchronously, so it reaches the handler, and it holds until the result is disposed.
/// </para>
/// </remarks>
public interface IBeginsWork
{
    /// <summary>
    /// Begins the work <paramref name="requirement"/> says the handler of <paramref name="request"/> runs in, or answers
    /// <see langword="null"/> where it begins none. The work ends when the result is disposed.
    /// </summary>
    /// <param name="requirement">A case of the request's requirement that this check decides, and that let the caller through.</param>
    /// <param name="request">The request whose checks let it through.</param>
    /// <param name="isQuery">
    /// Whether the request is a query. A query reads and changes nothing, so a check begins no work for it that changes
    /// anything.
    /// </param>
    IDisposable? BeginWork(AccessRequirement requirement, IRequireAccess request, bool isQuery);
}
