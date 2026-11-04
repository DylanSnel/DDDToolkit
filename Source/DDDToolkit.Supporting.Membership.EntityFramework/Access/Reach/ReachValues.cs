namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// What the conditions of a reach compare with, in one object. A condition reads each value as a member of this object,
/// so Entity Framework sends the value as a parameter. A compiled statement puts a parameter of its own in the place of the
/// caller and the moment, and constants in the place of the roles and the names
/// (<see cref="CompiledHolds{TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId}"/>).
/// </summary>
/// <typeparam name="TMemberId">Who a member is.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
/// <param name="members">The resource's collection of members, by the name of its property.</param>
/// <param name="owner">The resource's property that holds its owner, by its name.</param>
/// <param name="member">Who the caller is as a member.</param>
/// <param name="now">The moment a membership, and a role held in it, must apply at.</param>
/// <param name="roles">
/// The roles that give the permission: the ones the rules declare, as values, or, where the roles are rows, a query of the
/// statement's own context.
/// </param>
internal sealed class ReachValues<TMemberId, TRoleId>(string members, string owner, TMemberId member, DateTimeOffset now, IEnumerable<TRoleId> roles)
{
    /// <summary>The resource's collection of members, by the name of its property.</summary>
    public string Members { get; } = members;

    /// <summary>The resource's property that holds its owner, by its name.</summary>
    public string Owner { get; } = owner;

    /// <summary>Who the caller is as a member.</summary>
    public TMemberId Member { get; } = member;

    /// <summary>The moment a membership, and a role held in it, must apply at.</summary>
    public DateTimeOffset Now { get; } = now;

    /// <summary>The roles that give the permission.</summary>
    public IEnumerable<TRoleId> Roles { get; } = roles;
}
