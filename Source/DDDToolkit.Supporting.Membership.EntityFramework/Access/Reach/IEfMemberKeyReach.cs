using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>A reach for several keys as the statements of one kind of resource take it.</summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
internal interface IEfMemberKeyReach<TResource, TResourceId>
    where TResource : class
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>The keys held on those of <paramref name="resources"/> the caller sees, in a statement that runs on <paramref name="context"/>, or on the request's own.</summary>
    IQueryable<MemberKeyOn<TResourceId>> KeysOn(IQueryable<TResource> resources, DbContext? context);
}
