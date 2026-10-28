using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The questions of <see cref="MembershipAccessManager{TResource}"/> by the resource's id, for a caller that has only the
/// id, as a request does. Each answers what the question by the resource answers, and names the resource by the id it
/// was given:
/// <code>
/// var hold = await boardAccess.HoldAsync(command.Board, BoardPermissions.Edit, cancellationToken);   // a MemberHold&lt;BoardId&gt;
/// </code>
/// The resource's id is inferred from the argument, and has to be the id the resource is known by.
/// </summary>
public static class MembershipAccessManagerExtensions
{
    /// <summary>
    /// What the caller holds on the resource <paramref name="resource"/> names, in one statement:
    /// <see langword="null"/> when the caller does not see it, which is also the answer for a resource that does not
    /// exist; otherwise its id, the version it is at, and how the caller holds <paramref name="permission"/> on it and
    /// until when, if it does. It refuses nobody.
    /// </summary>
    /// <param name="access">The access manager of the resource's kind.</param>
    /// <param name="resource">The resource's id.</param>
    /// <param name="permission">A permission.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="access"/> is null.</exception>
    public static Task<MemberHold<TResourceId>?> HoldAsync<TResource, TResourceId>(
        this MembershipAccessManager<TResource> access,
        TResourceId resource,
        string permission,
        CancellationToken cancellationToken)
        where TResource : class, IEntity<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        => StorageOf<TResource, TResourceId>(access).HoldAsync(resource, permission, cancellationToken);

    /// <summary>
    /// Requires the caller to hold <paramref name="permission"/> on the resource <paramref name="resource"/> names: what a
    /// command or a query about one resource passes before its handler. One statement.
    /// </summary>
    /// <param name="access">The access manager of the resource's kind.</param>
    /// <param name="resource">The resource's id.</param>
    /// <param name="permission">The permission needed on it.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <returns>The resource's id, with the version it was checked at and how the permission is held.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>not-found</c> when it may not see the resource, for a missing one
    /// too; <c>not-permitted</c>, with the <c>Permission</c>, when it sees the resource but does not hold the permission.
    /// </exception>
    public static Task<MemberHold<TResourceId>> RequireAsync<TResource, TResourceId>(
        this MembershipAccessManager<TResource> access,
        TResourceId resource,
        string permission,
        CancellationToken cancellationToken)
        where TResource : class, IEntity<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        var storage = StorageOf<TResource, TResourceId>(access);
        return MembershipAccessManager.RequireAsync(storage.Rules, storage.RequireCurrentUser, () => storage.HoldAsync(resource, permission, cancellationToken), permission);
    }

    /// <summary>
    /// How the caller holds <paramref name="permission"/> on the resource <paramref name="resource"/> names, or
    /// <see langword="null"/> when it does not hold it. One statement.
    /// </summary>
    /// <param name="access">The access manager of the resource's kind.</param>
    /// <param name="resource">The resource's id.</param>
    /// <param name="permission">A permission.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>not-found</c> when the caller may not see the resource.
    /// </exception>
    public static Task<MemberVia?> ViaAsync<TResource, TResourceId>(
        this MembershipAccessManager<TResource> access,
        TResourceId resource,
        string permission,
        CancellationToken cancellationToken)
        where TResource : class, IEntity<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        var storage = StorageOf<TResource, TResourceId>(access);
        return MembershipAccessManager.ViaAsync(storage.Rules, storage.RequireCurrentUser, () => storage.HoldAsync(resource, permission, cancellationToken), permission);
    }

    /// <summary>
    /// The permissions of <paramref name="permissions"/> the caller holds on each of the resources
    /// <paramref name="resources"/> names that it sees, in one statement whatever their number, keyed by id. A resource the
    /// caller does not see, or holds none of the permissions on, is not in the answer, so asking about an id tells
    /// nothing about it.
    /// </summary>
    /// <param name="access">The access manager of the resource's kind.</param>
    /// <param name="resources">The resources' ids, at most <see cref="MembershipAccessManager.MostResources"/> different ones.</param>
    /// <param name="permissions">Permissions.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentException">
    /// More than <see cref="MembershipAccessManager.MostResources"/> different resources are asked about. A use case
    /// that takes the ids from a request refuses its caller, under a code of its own, before it asks.
    /// </exception>
    public static Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> PermissionsOnAsync<TResource, TResourceId>(
        this MembershipAccessManager<TResource> access,
        IReadOnlyCollection<TResourceId> resources,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
        where TResource : class, IEntity<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        => StorageOf<TResource, TResourceId>(access).PermissionsOnAsync(resources, permissions, cancellationToken);

    /// <summary>The storage the manager reads through, closed over the id it is asked by.</summary>
    /// <exception cref="ArgumentException">The resource is not known by <typeparamref name="TResourceId"/>.</exception>
    private static IMembershipStorage<TResource, TResourceId> StorageOf<TResource, TResourceId>(MembershipAccessManager<TResource> access)
        where TResource : class, IEntity<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(access);

        return access.Storage as IMembershipStorage<TResource, TResourceId>
            ?? throw new ArgumentException(
                "The access manager of " + typeof(TResource).Name + " is not asked by " + typeof(TResourceId).Name + ". Ask it by the id " + typeof(TResource).Name
                + " is known by, or by the resource itself.",
                nameof(access));
    }
}
