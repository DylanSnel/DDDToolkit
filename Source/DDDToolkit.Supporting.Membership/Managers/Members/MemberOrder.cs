using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// The one order every list of members is answered in: the owner first, then by when each membership starts, then by
/// member, and each member's roles by when each starts, then by role. An order of the members' own facts, the same
/// every time: the package has no names to order by, and a screen that shows names orders by them itself.
/// </summary>
internal static class MemberOrder
{
    /// <summary><paramref name="members"/> in the order, each with its roles in the order too.</summary>
    /// <param name="members">The members of one resource, read for showing.</param>
    /// <param name="owner">The resource's owner.</param>
    public static IReadOnlyList<TMember> Of<TMember, TId, TMemberId, TRoleId>(IEnumerable<TMember> members, TMemberId owner)
        where TMember : MemberEntity<TId, TMemberId, TRoleId>
        where TId : struct, IEntityId, IEquatable<TId>
        where TMemberId : struct, IEntityId, IEquatable<TMemberId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        var ordered = members
            .OrderByDescending(member => member.MemberId.Equals(owner))
            .ThenBy(member => member.StartsAt)
            .ThenBy(member => member.MemberId, IdOrder<TMemberId>.Instance)
            .ToList();

        foreach (var member in ordered)
        {
            member.OrderRoles(static (left, right) =>
            {
                var starts = left.StartsAt.CompareTo(right.StartsAt);
                return starts != 0 ? starts : IdOrder<TRoleId>.Instance.Compare(left.RoleId, right.RoleId);
            });
        }

        return ordered;
    }

    /// <summary>
    /// The order of two ids: the id's own, except that an id over a text is compared character by character. A text is
    /// otherwise compared by the language of the request it is asked in, so the same members would be listed in one order
    /// for an English reader and in another for a Danish one.
    /// </summary>
    private sealed class IdOrder<T> : IComparer<T>
    {
        public static readonly IdOrder<T> Instance = new();

        private static readonly bool OverText = typeof(IEntityId<string>).IsAssignableFrom(typeof(T));

        private static readonly bool Comparable = typeof(IComparable<T>).IsAssignableFrom(typeof(T)) || typeof(IComparable).IsAssignableFrom(typeof(T));

        public int Compare(T? left, T? right)
            => OverText
                ? string.CompareOrdinal(((IEntityId<string>)(object)left!).Value, ((IEntityId<string>)(object)right!).Value)
                : Comparable
                    ? Comparer<T>.Default.Compare(left, right)
                    : string.CompareOrdinal(left?.ToString(), right?.ToString());
    }
}
