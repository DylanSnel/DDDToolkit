using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Decides the requirements about one kind of resource with members (<see cref="MemberAccess{TResourceId}"/>)
/// before a handler runs. A module registers it for its request interface, once for each kind of resource its
/// requests are about:
/// <code>
/// services.AddAccessCheck&lt;IDocumentsRequest, MemberAccessCheck&lt;Document, DocumentId&gt;&gt;();
/// </code>
/// <para>
/// The order of what a caller is refused with is the same for every request: the caller's own refusal when it
/// is nobody; then <c>not-found</c> for a resource it may not see; then <c>not-permitted</c> for a key it does
/// not hold; then, for a command that names the version its caller read, a lost race when the resource is at
/// another one. Only then does the handler run, and whatever else it refuses comes after these.
/// </para>
/// <para>
/// A query that shows what a key is held on (<see cref="MemberAccess{TResourceId}.SeenWith"/>) refuses nobody
/// for want of the key: its own statement leaves out what the caller does not reach. Only a caller who did not
/// sign in is refused there, <c>not-permitted</c> naming the key, after the caller's own refusal: under no
/// rules is such a caller anybody's member or holds a key from above, so it reaches nothing, and its query is
/// not run at all. A database that checks every row may refuse such a caller outright, rather than answer
/// nothing, so its statement is never sent.
/// </para>
/// <para>
/// The handler needs nothing of it. It loads the resource its request names, which is the one that was
/// checked, with the version the caller named, <c>context.ExpectVersion(document, command.ExpectedVersion)</c>,
/// so a resource changed since the caller read it is a lost race at the load as well as here; the save compares
/// the version it loaded, the aggregate keeps its own rules, and a database that checks every row checks the
/// write again. A rule that needs how or until when the caller holds the key asks the questions for it
/// (<see cref="IMemberQuestions{TResourceId}.HoldAsync"/>).
/// </para>
/// <para>
/// What it read it keeps all the same, as a <see cref="MemberHold{TResourceId}"/> in <see cref="Checked{T}"/>,
/// with the request in hand: that costs nothing, and it is what the expert hold holds a save to, where a
/// context asks for it (<c>UseMemberHolds</c> of the Entity Framework package). Then a save that changes the
/// resource is refused when it is at another version than the check read, and one that changes a resource no
/// check of the request in hand read is refused outright.
/// </para>
/// <para>
/// It decides the cases of its own resource only. A module with two kinds of resource registers two, and a
/// requirement about a kind nobody registered a check for stops the request rather than letting it through.
/// </para>
/// </summary>
/// <typeparam name="TResource">The resource's aggregate, which a lost race names.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
/// <param name="access">The access questions of this kind of resource.</param>
/// <param name="kept">Where what was read of a request's resource is kept, for the expert hold.</param>
/// <param name="callers">Who is calling: what tells a caller who did not sign in.</param>
public sealed class MemberAccessCheck<TResource, TResourceId>(IMemberQuestions<TResourceId> access, Checked<MemberHold<TResourceId>> kept, ICallerAccessor callers) : IAccessCheck
    where TResource : class
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement) => requirement is MemberAccess<TResourceId>;

    /// <inheritdoc />
    /// <exception cref="RefusalException">The caller is not who the request requires.</exception>
    /// <exception cref="ConcurrencyConflictException">The resource is not at the version the request expects.</exception>
    /// <exception cref="InvalidOperationException">The requirement is not one of this check's cases.</exception>
    public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        switch (requirement)
        {
            case MemberAccess<TResourceId>.On required:
            {
                var hold = await access.RequireAsync(required.Resource, required.Key, cancellationToken).ConfigureAwait(false);

                // Only now, with the resource seen and the key held: a version that is not the one the caller
                // read means somebody changed the resource since. Asked any earlier, the answer would tell a
                // caller without access which version a resource is at.
                if (required.ExpectedVersion is { } expected && expected != hold.Version)
                {
                    throw new ConcurrencyConflictException(typeof(TResource), required.Resource);
                }

                // Kept under the request, and with it in hand: what the expert hold holds the save to, where a
                // context asks for it. The handler needs none of it.
                kept.KeepFor(request, hold);
                break;
            }

            case MemberAccess<TResourceId>.SeenWith required:
                // The key is the filter of the query's own statement. Here only: a caller the questions can be
                // asked about at all, and one that can reach something. A caller who did not sign in reaches
                // nothing under any rules, and the statement is not sent for it: a database that checks every
                // row may refuse that caller's role the resource's schema outright.
                access.RequireCaller();
                if (callers.Current.Kind == CallerKind.Anonymous)
                {
                    throw access.Rules.Codes.Refuse(MembershipRefusals.NotPermitted, ("Key", required.Key));
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"{request.GetType().Name} declares '{requirement}', which the access check of {typeof(TResource).Name} does not decide. "
                    + "A requirement nothing checks lets nobody through.");
        }
    }
}
