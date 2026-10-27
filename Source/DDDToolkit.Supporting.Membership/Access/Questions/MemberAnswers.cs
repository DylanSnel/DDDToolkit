using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// What the access manager of one kind of resource asks its storage, said of the resource itself: the storage closed
/// over the resource's id, which the manager, generic over the resource alone, cannot name.
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
internal abstract class MemberAnswers<TResource>
    where TResource : class
{
    /// <summary>The rules of the resource.</summary>
    public abstract MembershipRules Rules { get; }

    /// <summary>The storage, as it was handed over: what the questions by id ask, closed over the id they are given.</summary>
    public abstract object Storage { get; }

    /// <summary>Refuses a caller nothing can be answered for at all.</summary>
    public abstract void RequireCaller();

    /// <summary>The resources the caller reaches for <paramref name="permission"/>.</summary>
    public abstract MemberReach<TResource> Reach(string permission);

    /// <summary>The resources the caller sees.</summary>
    public abstract MemberReach<TResource> ReachSeen();

    /// <summary>What the caller holds on <paramref name="resource"/>, named by the resource itself.</summary>
    public abstract Task<MemberHold<TResource>?> HoldAsync(TResource resource, string permission, CancellationToken cancellationToken);

    /// <summary>The permissions held on each of <paramref name="resources"/> the caller sees, by the resources themselves.</summary>
    public abstract Task<IReadOnlyDictionary<TResource, IReadOnlySet<string>>> PermissionsOnAsync(
        IReadOnlyCollection<TResource> resources,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);
}

/// <summary><see cref="MemberAnswers{TResource}"/> over a storage of the resource and its id.</summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
/// <param name="storage">What the storage answers about the resource.</param>
internal sealed class MemberAnswers<TResource, TResourceId>(IMembershipStorage<TResource, TResourceId> storage) : MemberAnswers<TResource>
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <inheritdoc />
    public override MembershipRules Rules => storage.Rules;

    /// <inheritdoc />
    public override object Storage => storage;

    /// <inheritdoc />
    public override void RequireCaller() => storage.RequireCaller();

    /// <inheritdoc />
    public override MemberReach<TResource> Reach(string permission) => storage.Reach(permission);

    /// <inheritdoc />
    public override MemberReach<TResource> ReachSeen() => storage.ReachSeen();

    /// <inheritdoc />
    public override async Task<MemberHold<TResource>?> HoldAsync(TResource resource, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        // Asked by the resource's id, which is all a statement compares, and answered about the resource that was given.
        var hold = await storage.HoldAsync(resource.Id, permission, cancellationToken).ConfigureAwait(false);
        return hold is null ? null : new MemberHold<TResource>(resource, hold.Via, hold.Version, hold.Until);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyDictionary<TResource, IReadOnlySet<string>>> PermissionsOnAsync(
        IReadOnlyCollection<TResource> resources,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Any(resource => resource is null))
        {
            throw new ArgumentException("A resource asked about is not null.", nameof(resources));
        }

        // The resources that were given, by their ids: two objects of one resource are one resource, and either answers for it.
        var given = new Dictionary<TResourceId, TResource>();
        foreach (var resource in resources)
        {
            given.TryAdd(resource.Id, resource);
        }

        var held = await storage.PermissionsOnAsync(given.Keys, permissions, cancellationToken).ConfigureAwait(false);
        return held.ToDictionary(pair => given[pair.Key], pair => pair.Value);
    }
}
