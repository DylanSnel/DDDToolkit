using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// What a request about a resource with members requires of its caller, said where the request is declared:
/// <code>
/// public sealed record ShareDocument(DocumentId Document, UserId With, NamedRole Role, long? ExpectedVersion = null) : IDocumentsRequest
/// {
///     AccessRequirement IRequireAccess.RequiredAccess =&gt; MemberAccess.On(DocumentKeys.Share, Document, ExpectedVersion);
/// }
/// </code>
/// The cases are <see cref="MemberAccess{TResourceId}"/>'s, closed over the id of the resource the request is
/// about, which is what tells the requirements of one kind of resource from those of another:
/// <see cref="MemberAccessCheck{TResource, TResourceId}"/> decides the cases of its own resource and no other.
/// <para>
/// Which key a request requires is the application's to say, for the commands that change the members as
/// for any other: the package has no key of its own for adding a member, giving a role or handing a resource
/// on. A command an application wants only some callers to run requires a key those callers hold.
/// </para>
/// </summary>
public static class MemberAccess
{
    /// <summary>
    /// The caller holds <paramref name="key"/> on <paramref name="resource"/>, through its members or however
    /// else the resource's rules let a key be held.
    /// </summary>
    /// <param name="key">The key the request needs on the resource.</param>
    /// <param name="resource">The resource, from the request.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, for a command that says so.</param>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    public static MemberAccess<TResourceId>.On On<TResourceId>(string key, TResourceId resource, long? expectedVersion = null)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        => new(key, resource, expectedVersion);

    /// <summary>
    /// The caller is shown only the resources it holds <paramref name="key"/> on. For queries only.
    /// </summary>
    /// <param name="key">The key that decides what the query shows.</param>
    /// <typeparam name="TResourceId">The id of the kind of resource the query is about.</typeparam>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    public static MemberAccess<TResourceId>.SeenWith SeenWith<TResourceId>(string key)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        => new(key);
}
