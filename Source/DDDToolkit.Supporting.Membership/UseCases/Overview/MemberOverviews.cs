using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.UseCases;

/// <summary>
/// What turns the members a query read into what a caller is shown, in the one order every answer lists
/// members in. It reads nothing, so it costs no statement.
/// </summary>
public static class MemberOverviews
{
    /// <summary>
    /// The members of a resource: the owner first, then by when each membership starts, then by member, and
    /// each member's roles by when each starts, then by role.
    /// <para>
    /// An order of the members' own facts, the same every time: the package has no names to order by, and a
    /// screen that shows names orders by them itself. An id over a text, such as a named role, is ordered by
    /// its characters and not by the language of whoever asks, so a list reads the same for every reader. A
    /// role is said to count now only when its own period applies and the membership it is held in does.
    /// </para>
    /// </summary>
    /// <param name="members">The members, as a query read them: the resource's own member collection.</param>
    /// <param name="owner">The resource's owner.</param>
    /// <param name="now">The moment a membership, and a role held in it, must apply at to count now.</param>
    /// <typeparam name="TId">The member class's own id.</typeparam>
    /// <typeparam name="TMemberId">What a member is known by.</typeparam>
    /// <typeparam name="TRoleId">What a role is known by.</typeparam>
    public static IReadOnlyList<MemberOverview<TMemberId, TRoleId>> Of<TId, TMemberId, TRoleId>(
        IEnumerable<MemberEntity<TId, TMemberId, TRoleId>> members,
        TMemberId owner,
        DateTimeOffset now)
        where TId : struct, IEntityId, IEquatable<TId>
        where TMemberId : struct, IEntityId, IEquatable<TMemberId>, IComparable<TMemberId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>, IComparable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(members);

        return [.. members
            .OrderByDescending(member => member.MemberId.Equals(owner))
            .ThenBy(member => member.StartsAt)
            .ThenBy(member => member.MemberId, IdOrder<TMemberId>.Instance)
            .Select(member =>
            {
                var applies = member.AppliesAt(now);
                return new MemberOverview<TMemberId, TRoleId>(
                    member.MemberId,
                    member.MemberId.Equals(owner),
                    member.StartsAt,
                    member.EndsAt,
                    applies,
                    [.. member.Roles
                        .OrderBy(held => held.StartsAt)
                        .ThenBy(held => held.RoleId, IdOrder<TRoleId>.Instance)
                        .Select(held => new MemberRoleOverview<TRoleId>(held.RoleId, held.StartsAt, held.EndsAt, applies && held.AppliesAt(now)))]);
            })];
    }

    /// <summary>
    /// The order of two ids: the id's own, except that an id over a text is compared character by character.
    /// A text is otherwise compared by the language of the request it is asked in, so the same members would
    /// be listed in one order for an English reader and in another for a Danish one.
    /// </summary>
    private sealed class IdOrder<T> : IComparer<T>
        where T : struct, IComparable<T>
    {
        public static readonly IdOrder<T> Instance = new();

        private static readonly bool OverText = typeof(IEntityId<string>).IsAssignableFrom(typeof(T));

        public int Compare(T left, T right)
            => OverText
                ? string.CompareOrdinal(((IEntityId<string>)(object)left).Value, ((IEntityId<string>)(object)right).Value)
                : left.CompareTo(right);
    }
}
