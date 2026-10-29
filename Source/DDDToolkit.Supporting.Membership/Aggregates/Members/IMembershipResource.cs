namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// A resource with members, as the package's generator writes it onto the aggregate root a member class names: its
/// name, the codes it refuses under, and the scope of its module's own system work. The rules of the resource read
/// all three through its type, <c>MembershipRules.For&lt;Board&gt;(...)</c>, so none of them is written twice.
/// <code>
/// [Member&lt;BoardMemberId, SeatId, BoardRoleId, Board&gt;]
/// public sealed partial class BoardMember;
///
/// // Written onto Board: MembershipName "boards", the codes boards.already-member and the rest, the scope "boards".
/// </code>
/// An application declares nothing for it. The name follows from the class, <c>Board</c> as <c>boards</c> and
/// <c>CraneBoard</c> as <c>crane-boards</c>, unless the class says another with <see cref="MembersAttribute"/>. The
/// codes are a <c>Codes</c> property the generator writes, <see cref="MembershipCodes.Under"/> the name, unless the
/// class declares a static <see cref="Membership.MembershipCodes"/> of its own, which is then what it refuses under.
/// </summary>
public interface IMembershipResource
{
    /// <summary>
    /// The resource's name, as its codes and its functions in the database are named: <c>boards</c>, so the codes are
    /// <c>boards.already-member</c> and so on, and the functions <c>boards_i_see</c> and so on.
    /// </summary>
    static abstract string MembershipName { get; }

    /// <summary>The codes the resource refuses under: the member list's, the questions' and the member manager's.</summary>
    static abstract MembershipCodes MembershipCodes { get; }

    /// <summary>
    /// The scope of the system work of the module that declares the resource, its name as a scope is written:
    /// <c>boards</c> for the module <c>Boards</c>. <see langword="null"/> for a resource of a project that declares no
    /// module. Work begun in this scope holds every permission on the resource unless its rules say otherwise.
    /// </summary>
    static abstract string? MembershipScope { get; }
}
