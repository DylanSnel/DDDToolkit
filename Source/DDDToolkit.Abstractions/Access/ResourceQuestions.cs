using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// The questions a row access rule asks about a resource, by the resource's id. A module publishes the id in its
/// contracts, and that is all a rule of any module needs:
/// <code>
/// public static bool Allows(Checklist checklist, IRowUser user) =&gt; user.Sees&lt;BoardId&gt;().Contains(checklist.BoardId);
/// public static bool Allows(Board board, IRowUser user) =&gt; user.Holds&lt;BoardId&gt;(BoardPermissions.Edit).Contains(board.Id);
/// public static bool Allows(Seat seat, IRowUser user) =&gt; user.FellowMembers&lt;BoardId, SeatId&gt;().Contains(seat.Id);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Each question names no function. The generator writes it as the resource's id and the set,
/// <c>{fn:@Boards.Contracts.BoardId/seen}</c>, and the export resolves that to the function a contribution says answers
/// the set for that resource, under whatever name the package that keeps the resource's access gives it. A rule that
/// asks a set no contribution answers is refused when its access file is written, naming the rule.
/// </para>
/// <para>
/// Each question answers only for the caller of the connection, so anyone who sees the id may ask it. Only the database
/// answers: called in C#, each throws <see cref="DatabaseOnlyException"/>. They are extension methods on
/// <see cref="IRowUser"/>, so they cannot be called where there is no rule.
/// </para>
/// </remarks>
public static class ResourceQuestions
{
    /// <summary>
    /// The ids of the resources the caller sees. A resource with members is seen by its members, by its owner, and, where
    /// its rules let a permission held above reach it, by whoever holds the permission that sees it there.
    /// </summary>
    /// <typeparam name="TKey">The id of the resource's aggregate.</typeparam>
    /// <param name="user">The rule's user.</param>
    /// <returns>Never in C#: a rule asks the answer's <c>Contains</c>, in SQL.</returns>
    /// <exception cref="DatabaseOnlyException">Always: the question is the database's.</exception>
    public static AccessSet<TKey> Sees<TKey>(this IRowUser user)
        where TKey : struct, IEntityId
        => throw new DatabaseOnlyException(NameOf<TKey>("seen") + "()");

    /// <summary>
    /// The ids of the resources on which the caller holds <paramref name="permission"/>. On a resource with members a
    /// permission is held through a role, by being a member for the permission that being one gives, by owning the
    /// resource, or from above.
    /// </summary>
    /// <typeparam name="TKey">The id of the resource's aggregate.</typeparam>
    /// <param name="user">The rule's user.</param>
    /// <param name="permission">A permission.</param>
    /// <returns>Never in C#: a rule asks the answer's <c>Contains</c>, in SQL.</returns>
    /// <exception cref="DatabaseOnlyException">Always: the question is the database's.</exception>
    public static AccessSet<TKey> Holds<TKey>(this IRowUser user, string permission)
        where TKey : struct, IEntityId
        => throw new DatabaseOnlyException(NameOf<TKey>("held_on") + "(permission)");

    /// <summary>
    /// The ids of the members of every resource the caller is a member of now, the caller among them: the people on the
    /// projects a seat works on, which a read rule on the application's seat class asks to let a seat read who it works
    /// with.
    /// </summary>
    /// <typeparam name="TKey">The id of the resource's aggregate.</typeparam>
    /// <typeparam name="TMemberId">The id the members are known by, such as <c>SeatId</c>, which the resource's id does not say.</typeparam>
    /// <param name="user">The rule's user.</param>
    /// <returns>Never in C#: a rule asks the answer's <c>Contains</c>, in SQL.</returns>
    /// <exception cref="DatabaseOnlyException">Always: the question is the database's.</exception>
    public static AccessSet<TMemberId> FellowMembers<TKey, TMemberId>(this IRowUser user)
        where TKey : struct, IEntityId
        where TMemberId : struct, IEntityId
        => throw new DatabaseOnlyException(NameOf<TKey>("fellow_members") + "()");

    private static string NameOf<TKey>(string set) => "@" + typeof(TKey).FullName + "/" + set;
}
