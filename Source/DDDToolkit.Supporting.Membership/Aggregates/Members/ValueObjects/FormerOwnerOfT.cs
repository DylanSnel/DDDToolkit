using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership;

/// <summary>What the owner before becomes when somebody else is named owner: see <see cref="FormerOwner"/>.</summary>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
public sealed class FormerOwner<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private static readonly FormerOwner<TRoleId> Leaving = new(null);

    internal FormerOwner(TRoleId? role) => Role = role;

    /// <summary>The role the owner before holds from now on, or <see langword="null"/> when it leaves the list.</summary>
    public TRoleId? Role { get; }

    /// <summary>The owner before leaves the list, whatever the role is known by.</summary>
    /// <param name="leaves"><see cref="FormerOwner.Leaves"/>.</param>
    public static implicit operator FormerOwner<TRoleId>(FormerOwnerLeaves leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);

        return Leaving;
    }
}
