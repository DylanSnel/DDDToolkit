using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>A reach for several permissions as the statements of one kind of resource take it.</summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
internal interface IEfMemberPermissionReach<TResource, TResourceId>
    where TResource : class
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>The permissions held on those of <paramref name="resources"/> the caller sees, in a statement that runs on <paramref name="context"/>, or on the request's own.</summary>
    IQueryable<MemberPermissionOn<TResourceId>> PermissionsOn(IQueryable<TResource> resources, DbContext? context);
}
