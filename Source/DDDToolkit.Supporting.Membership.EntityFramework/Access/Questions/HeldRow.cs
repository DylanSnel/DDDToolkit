namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// What a compiled check reads of a resource: its id and version, how the caller holds the permission there, and until
/// when (<see cref="CompiledHolds{TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId}"/>).
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
internal sealed class HeldRow<TResourceId>
    where TResourceId : struct
{
    /// <summary>The resource's id, where several are read at once.</summary>
    public TResourceId Id { get; init; }

    /// <summary>The resource's version.</summary>
    public long Version { get; init; }

    /// <summary>Whether the caller holds the permission as one of the resource's members, or as its owner.</summary>
    public bool AsMember { get; init; }

    /// <summary>Whether the caller holds the permission where the resource sits, or above it.</summary>
    public bool FromAbove { get; init; }

    /// <summary>Until when the caller holds the permission as a member, or <see langword="null"/> for no end.</summary>
    public DateTimeOffset? Until { get; init; }
}
