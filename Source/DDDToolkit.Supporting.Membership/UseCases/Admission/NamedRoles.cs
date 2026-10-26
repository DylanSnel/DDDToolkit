using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.UseCases;

/// <summary>
/// The roles a resource's rules declare, as the admission asks about them: for a resource whose members hold
/// <see cref="NamedRole"/>s. It reads nothing but the rules.
/// </summary>
/// <typeparam name="TResourceId">The id of the kind of resource the members are of.</typeparam>
/// <param name="rules">The resource's rules.</param>
public sealed class NamedRoles<TResourceId>(MembershipRules rules) : IMemberRoles<TResourceId, NamedRole>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    private readonly MembershipRules _rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(NamedRole role, CancellationToken cancellationToken) => ValueTask.FromResult(_rules.Knows(role));

    /// <inheritdoc />
    public ValueTask<NamedRole?> FindOwnerRoleAsync(CancellationToken cancellationToken)
    {
        var owners = new NamedRole(_rules.OwnerRole);
        return ValueTask.FromResult<NamedRole?>(_rules.Knows(owners) ? owners : null);
    }
}
