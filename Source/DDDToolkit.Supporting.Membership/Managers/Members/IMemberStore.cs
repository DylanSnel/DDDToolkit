using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// Where the member manager of one kind of resource loads, adds and saves the resource: the unit of work of the scope.
/// A storage package implements it and registers it with the resource; an application never takes it.
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
/// <typeparam name="TMemberId">What a member is known by.</typeparam>
public interface IMemberStore<TResource, TResourceId, TMemberId>
    where TResource : class, IEntity<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
{
    /// <summary>
    /// Who the current caller is as a member of the resource, as its rules say, or <see langword="null"/> for a caller
    /// that is nobody's member, system work among them: who a change is recorded as made by.
    /// </summary>
    TMemberId? CurrentMember { get; }

    /// <summary>
    /// The resource with its members, tracked by the unit of work for a change and its save; or <see langword="null"/>
    /// when it is not there for the caller.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="expectedVersion">
    /// The version the caller last read, or <see langword="null"/> to change the resource as it is now: a resource at
    /// another version is a lost race.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The resource is not at <paramref name="expectedVersion"/>.</exception>
    Task<TResource?> FindAsync(TResourceId resource, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// The resources of <paramref name="resources"/> within <paramref name="reach"/>, each with its members, read for
    /// showing: the unit of work does not track them.
    /// </summary>
    /// <param name="resources">The resources asked about.</param>
    /// <param name="reach">The resources the caller sees, as the resource's access manager made it.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<TResource>> ReadAsync(IReadOnlyCollection<TResourceId> resources, MemberReach<TResource> reach, CancellationToken cancellationToken);

    /// <summary>Adds a new resource to the unit of work.</summary>
    /// <param name="resource">The resource, just made.</param>
    void Add(TResource resource);

    /// <summary>
    /// What the unit of work would write besides <paramref name="parts"/>: the name of each kind of thing it holds a
    /// change to that is none of them, or none.
    /// </summary>
    /// <param name="parts">The resource, its members and the roles they hold, each by reference.</param>
    IReadOnlyList<string> ChangedBesides(IReadOnlySet<object> parts);

    /// <summary>Saves everything the unit of work holds, with the domain events raised, in one transaction.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <exception cref="Exceptions.ConcurrencyConflictException">Somebody else saved the same resource first.</exception>
    Task SaveAsync(CancellationToken cancellationToken);
}
