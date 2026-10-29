using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The queries on the set of one kind of resource, for the current caller at this moment, as the extensions on its set
/// ask them (<see cref="MemberQueryableExtensions"/>): closed over every type of the resource, which the extensions,
/// generic over the resource alone, cannot name.
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
internal interface IMemberQueries<TResource>
    where TResource : class
{
    /// <summary>Those of <paramref name="resources"/> the caller holds <paramref name="permission"/> on, in a statement that runs on <paramref name="context"/>.</summary>
    IQueryable<TResource> WhereIHold(IQueryable<TResource> resources, string permission, DbContext context);

    /// <summary>The pairs of resource and permission the caller holds of <paramref name="permissions"/>, among those of <paramref name="resources"/> it sees.</summary>
    IQueryable<PermissionOn<TResource>> WhereIHold(IQueryable<TResource> resources, IReadOnlyCollection<string> permissions, DbContext context);
}
