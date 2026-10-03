using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// What a reach reads of a resource: its members and its owner, by the names of their properties, where it
/// sits for one reached from above, and the rows of its roles for one whose roles are kept.
/// </summary>
/// <param name="Members">The resource's collection of members.</param>
/// <param name="Owner">The resource's property that holds its owner.</param>
/// <param name="Above">How a key held from above is compared with where the resource sits, or <see langword="null"/> for a resource that is not reached from above.</param>
/// <param name="Roles">The rows of the resource's roles, or <see langword="null"/> for a resource whose roles are not kept for it.</param>
/// <param name="FollowTheRequest">
/// Whether the rows a question reads, the resource's or its kept roles', are under a query filter that reads
/// a member of the context it runs on: such a question is read on the request's own context.
/// </param>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
internal sealed record MemberNames<TResource, TRoleId>(string Members, string Owner, PlacesAbove<TResource>? Above, KeptRoles<TRoleId>? Roles, bool FollowTheRequest)
    where TResource : class
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
