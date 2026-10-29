using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.Supporting.Membership.Tests.Support;

/// <summary>
/// Where a member manager loads and saves, kept in memory: the resources a test puts in it, the caller as a member, and
/// who each save ran as.
/// </summary>
public sealed class StubMemberStore<TResource, TResourceId, TMemberId> : IMemberStore<TResource, TResourceId, TMemberId>
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
{
    /// <summary>The resources there are.</summary>
    public Dictionary<TResourceId, TResource> Kept { get; } = [];

    /// <inheritdoc />
    public TMemberId? CurrentMember { get; set; }

    /// <summary>What the unit of work holds besides the resource a save is about.</summary>
    public List<string> Besides { get; } = [];

    /// <summary>Who each save ran as: the user begun for it, or <see langword="null"/> where it ran as the caller.</summary>
    public List<CurrentUserSnapshot?> Saves { get; } = [];

    /// <summary>Puts <paramref name="resource"/> in the store.</summary>
    public StubMemberStore<TResource, TResourceId, TMemberId> With(TResource resource)
    {
        Kept[resource.Id] = resource;
        return this;
    }

    /// <inheritdoc />
    public Task<TResource?> FindAsync(TResourceId resource, long? expectedVersion, CancellationToken cancellationToken)
        => Task.FromResult(Kept.TryGetValue(resource, out var found) ? found : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<TResource>> ReadAsync(IReadOnlyCollection<TResourceId> resources, Access.MemberReach<TResource> reach, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TResource>>([.. resources.Where(Kept.ContainsKey).Select(id => Kept[id])]);

    /// <inheritdoc />
    public void Add(TResource resource) => Kept[resource.Id] = resource;

    /// <inheritdoc />
    public IReadOnlyList<string> ChangedBesides(IReadOnlySet<object> parts) => Besides;

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken)
    {
        Saves.Add(BegunUser);
        return Task.CompletedTask;
    }
}

/// <summary>Where a role manager loads and saves roles, kept in memory.</summary>
public sealed class StubRoleStore<TRole, TRoleId> : ICustomRoleStore<TRole, TRoleId>
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The roles there are.</summary>
    public List<TRole> Kept { get; } = [];

    /// <summary>How many times the unit of work was saved.</summary>
    public int Saves { get; private set; }

    /// <inheritdoc />
    public Task<TRole?> FindAsync(TRoleId role, CancellationToken cancellationToken) => Task.FromResult(Kept.FirstOrDefault(kept => kept.Id.Equals(role)));

    /// <inheritdoc />
    public Task<IReadOnlyList<TRole>> ListAsync(System.Linq.Expressions.Expression<Func<TRole, bool>>? scope, bool tracked, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TRole>>(scope is null ? [.. Kept] : [.. Kept.AsQueryable().Where(scope)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<TRole>> ByIdAsync(IReadOnlyCollection<TRoleId> roles, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TRole>>([.. Kept.Where(kept => roles.Contains(kept.Id))]);

    /// <inheritdoc />
    public void Add(TRole role) => Kept.Add(role);

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>A clock that says one moment.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;
}
