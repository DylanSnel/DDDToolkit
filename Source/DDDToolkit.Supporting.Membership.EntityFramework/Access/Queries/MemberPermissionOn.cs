using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// A permission the caller holds on a resource: one row of a statement that answers which permissions are held on which
/// resources. A permission is one row on a resource, however many ways the caller holds it there.
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public sealed class MemberPermissionOn<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>The resource.</summary>
    public required TResourceId Resource { get; init; }

    /// <summary>The permission held on it.</summary>
    public required string Permission { get; init; }
}
