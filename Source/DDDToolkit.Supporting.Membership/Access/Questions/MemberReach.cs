using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which resources of one kind the caller reaches for one permission, at one moment: what a module puts into a
/// statement of its own, so "the documents I may see" is one query and never a list of ids fetched first.
/// <para>
/// It is made by the access questions of the resource (<see cref="IMemberQuestions{TResourceId}.Reach"/>), for
/// the caller the request runs as, and the storage those questions read through is what puts it into a query.
/// What a query is filtered by is therefore always what the rules decided, and never a set somebody put
/// together elsewhere: a storage takes only the reaches its own questions made. What is public here is what a
/// use case reads of it.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public abstract class MemberReach<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>For the access questions of a storage.</summary>
    /// <param name="permission">The permission the reach is for.</param>
    /// <param name="now">The moment a membership, and a role held in it, must apply at.</param>
    /// <param name="everything">Whether the caller reaches every resource: the application's own work.</param>
    protected MemberReach(string permission, DateTimeOffset now, bool everything)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        Permission = permission;
        Now = now;
        Everything = everything;
    }

    /// <summary>
    /// For the access questions of a storage: the reach of what the caller sees, under rules that name no permission
    /// for seeing (<see cref="MembershipRules.EveryMemberPermission"/>). A resource is seen by its members and its owner all
    /// the same, so there is such a reach, and no permission it is for.
    /// </summary>
    /// <param name="now">The moment a membership must apply at.</param>
    /// <param name="everything">Whether the caller reaches every resource: the application's own work.</param>
    protected MemberReach(DateTimeOffset now, bool everything)
    {
        Now = now;
        Everything = everything;
    }

    /// <summary>
    /// The permission the reach is for. <see langword="null"/> only for the reach of what the caller sees
    /// (<see cref="MemberPermissionReach{TResourceId}.See"/>) under rules that name no permission for seeing.
    /// </summary>
    public string? Permission { get; }

    /// <summary>
    /// The moment a membership, and a role held in it, must apply at to count. One moment for the whole
    /// statement, and the one an answer made from it compares periods with.
    /// </summary>
    public DateTimeOffset Now { get; }

    /// <summary>Whether the caller is the application's own work, which reaches every resource: a statement then leaves the filter out.</summary>
    public bool Everything { get; }

    /// <summary>
    /// How a resource read with this reach is reached, from what the statement found. The members come first:
    /// a member that also holds the permission from above reaches the resource as a member.
    /// </summary>
    /// <param name="asMember">Whether the statement found the caller among the members, now, with what this reach asks of a membership.</param>
    /// <param name="fromAbove">Whether the statement found the permission held where the resource sits, or above it.</param>
    /// <returns>How, or <see langword="null"/> when the resource is not within this reach.</returns>
    public MemberVia? Via(bool asMember, bool fromAbove = false)
        => Everything ? MemberVia.System
            : asMember ? MemberVia.Members
            : fromAbove ? MemberVia.Above
            : null;
}
