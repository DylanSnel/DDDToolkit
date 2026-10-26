using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// What a request about a resource with members requires of its caller, said where the request is declared:
/// <code>
/// public sealed record ShareDocument(DocumentId Document, UserId With, NamedRole Role, long? ExpectedVersion = null) : IDocumentsRequest
/// {
///     AccessRequirement IRequireAccess.RequiredAccess =&gt; MemberAccess.On(DocumentPermissions.Share, Document, ExpectedVersion);
/// }
/// </code>
/// The cases are <see cref="MemberAccess{TResourceId}"/>'s, closed over the id of the resource the request is
/// about, which is what tells the requirements of one kind of resource from those of another:
/// <see cref="MemberAccessCheck{TResource, TResourceId}"/> decides the cases of its own resource and no other.
/// <para>
/// Where the requirement reads no value of the request but the permission, the resource and the version it carries, the
/// request says it with attributes instead, and the toolkit's generator writes the same call:
/// <code>
/// [RequiresPermission(DocumentPermissions.Share)]
/// public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, NamedRole Role, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
///
/// [SeenWith&lt;DocumentId&gt;(DocumentPermissions.View)]
/// public sealed record ListDocuments : IDocumentsRequest;
/// </code>
/// (<see cref="OnResourceAttribute"/>, <see cref="ExpectedVersionAttribute"/>, <see cref="SeenWithAttribute{TResourceId}"/>).
/// These methods stay for a requirement computed from the request's values.
/// </para>
/// <para>
/// Which permission a request requires is the application's to say, for the commands that change the members as
/// for any other: the package has no permission of its own for adding a member, giving a role or handing a resource
/// on. A command an application wants only some callers to run requires a permission those callers hold.
/// </para>
/// </summary>
public static class MemberAccess
{
    /// <summary>
    /// The caller holds <paramref name="permission"/> on <paramref name="resource"/>, through its members or however
    /// else the resource's rules let a permission be held.
    /// </summary>
    /// <param name="permission">The permission the request needs on the resource.</param>
    /// <param name="resource">The resource, from the request.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, for a command that says so.</param>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
    public static MemberAccess<TResourceId>.On On<TResourceId>(string permission, TResourceId resource, long? expectedVersion = null)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        => new(permission, resource, expectedVersion);

    /// <summary>
    /// The caller is shown only the resources it holds <paramref name="permission"/> on. For queries only.
    /// </summary>
    /// <param name="permission">The permission that decides what the query shows.</param>
    /// <typeparam name="TResourceId">The id of the kind of resource the query is about.</typeparam>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
    public static MemberAccess<TResourceId>.SeenWith SeenWith<TResourceId>(string permission)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        => new(permission);

    /// <summary>
    /// The caller sees <paramref name="resource"/>, whether or not it holds <paramref name="permission"/> there: what a request
    /// about the caller's own on the resource requires of it, beside the caller's own, where the permission is the other way
    /// through. <c>[OrOwn]</c> beside <c>[OnResource]</c> says it:
    /// <code>
    /// [RequiresPermission(DocumentPermissions.Share)]                                    // take yourself off, or anybody with the permission
    /// public sealed record LeaveDocument([OnResource] DocumentId Document, [OrOwn] UserId Member) : IDocumentsRequest;
    /// </code>
    /// is <c>AccessRequirement.Any(AccessRequirement.All(AccessRequirement.Own(request.Member), MemberAccess.Sees(DocumentPermissions.Share,
    /// request.Document)), MemberAccess.On(DocumentPermissions.Share, request.Document))</c>.
    /// </summary>
    /// <param name="permission">The permission the other way through requires on the resource: asked about, and not required.</param>
    /// <param name="resource">The resource, from the request.</param>
    /// <param name="expectedVersion">The version of the resource the caller last read, for a command that says so.</param>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
    public static MemberAccess<TResourceId>.Sees Sees<TResourceId>(string permission, TResourceId resource, long? expectedVersion = null)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        => new(permission, resource, expectedVersion);
}
