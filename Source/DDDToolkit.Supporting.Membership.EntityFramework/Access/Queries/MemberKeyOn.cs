using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// A key the caller holds on a resource: one row of a statement that answers which keys are held on which
/// resources. A key is one row on a resource, however many ways the caller holds it there.
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public sealed class MemberKeyOn<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>The resource.</summary>
    public required TResourceId Resource { get; init; }

    /// <summary>The key held on it.</summary>
    public required string Key { get; init; }
}
