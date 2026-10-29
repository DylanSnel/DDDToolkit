using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// What the owner before becomes when somebody else is named owner, said every time, since an owner holds no role
/// and nothing can be taken for granted: a member in a role, or nobody on the list.
/// <code>
/// await boardMembers.NameOwnerAsync(board, seat, FormerOwner.StaysAs(editorRole), cancellationToken);    // stays on the board, as an editor
/// await boardMembers.NameOwnerAsync(board, seat, FormerOwner.Leaves, cancellationToken);                 // leaves the board
/// </code>
/// </summary>
public static class FormerOwner
{
    /// <summary>The owner before goes off the list, as a member removed does.</summary>
    public static FormerOwnerLeaves Leaves { get; } = new();

    /// <summary>
    /// The owner before stays a member, with no end, holding <paramref name="role"/> from the moment the owner is
    /// named: a role id where the roles are rows, a <see cref="NamedRole"/> where they are fixed.
    /// </summary>
    /// <param name="role">The role; the member manager checks it may go to a member.</param>
    /// <typeparam name="TRoleId">What a role is known by.</typeparam>
    public static FormerOwner<TRoleId> StaysAs<TRoleId>(TRoleId role)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        => new(role);
}
