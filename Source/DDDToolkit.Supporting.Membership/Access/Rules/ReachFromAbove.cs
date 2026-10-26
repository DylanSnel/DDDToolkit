using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Says that a resource is reached from above as well as through its members: it hangs at a place, a unit of an
/// organization or a bay of a depot, which its class marks <c>[At]</c>, and a caller that holds a key at that place,
/// or above it, holds the key on the resource without being a member of it.
/// <code>
/// // Beside a package that keeps an organization: where a key is held is that package's to answer, and the rules
/// // say only that it reaches.
/// MembershipRules.For&lt;Board&gt;(Roles.Custom(...), reachFromAbove: true)
///
/// // Beside a place of the application's own: the function that answers it, which the application declares once.
/// MembershipRules.For&lt;Crate&gt;(Roles.Fixed(...), reachFromAbove: DepotFunctions.BaysWhereIHold)
/// </code>
/// <para>
/// Two pictures. Beside an organization, Ada holds the administrators' role at its root, and reaches every board of
/// the organization without being on one; Ben holds <c>boards.edit</c> at Pier 7, and edits the boards that hang at
/// Pier 7, and no other. Without one, in a depot of the application's own, porter Piet holds <c>crates.scrap</c> at
/// bay A, and scraps crate 17, which stands there, without being one of its porters.
/// </para>
/// <para>
/// Why it matters: whoever runs a part of an organization sees and acts on what hangs there, without being added to
/// each resource; and a resource whose owner has left is still reached by somebody, who can hand it to a new owner.
/// Through its members alone, a board whose owner left would be nobody's to hand on.
/// </para>
/// <para>
/// A resource the caller does not see does not exist for it, whatever it holds there. So rules that let a resource be
/// reached from above have a permission every member holds (<see cref="OnAttribute{TResource}.EveryMember"/>, which is
/// required then): whoever holds that permission where the resource hangs sees the resource, as its members do, and
/// without one nobody above would.
/// </para>
/// <para>
/// A key held from above is not cut by what a role gives (<see cref="OnAttribute{TResource}.InRoles"/>): that is about
/// a member's roles on the resource, and a key held above is held through the place, by a role of the organization, a
/// permission marked <c>InRoles = false</c> included. Only a permission marked <c>FromAbove = false</c> is not reached
/// this way. A caller that holds a key both ways holds it as a member (<see cref="MemberVia.Members"/>).
/// </para>
/// <para>
/// Where a caller holds a key is the application's to answer, or the organization package's: in C# through an
/// <c>IPlacesReached&lt;TResourceId, TPlaceId&gt;</c> registered with the resource, which the package that stores the
/// resources asks inside its own statement, and which such a package's join with this one writes; in a database that
/// answers the questions itself, through a function. Beside such a package that function is its own, found by the type
/// of the place, its unit id; beside a place of the application's own it is the one named here. The package keeps no places and no
/// tree of them.
/// </para>
/// </summary>
public sealed class ReachFromAbove
{
    private ReachFromAbove(DatabaseFunction? function) => Function = function;

    /// <summary>
    /// The function that answers in the database where the caller holds a key, or <see langword="null"/> where it
    /// follows from the type of the place: the one the organization package's contribution names for its unit id. A function that takes
    /// the key as text and answers the places the caller's hold of it reaches, each place it is held at and every place
    /// below. It is asked by the resource's functions, which run as their owner, so it answers for the caller by a
    /// condition of its own, never by a policy.
    /// </summary>
    public DatabaseFunction? Function { get; }

    /// <summary>
    /// <c>reachFromAbove: true</c>: a resource is reached from above, by the function that follows from the type of the
    /// place it hangs at. <c>false</c> is no reach from above, as leaving the argument out is.
    /// </summary>
    /// <param name="reaches">Whether a key held above the resource reaches it.</param>
    public static implicit operator ReachFromAbove?(bool reaches) => reaches ? new ReachFromAbove(null) : null;

    /// <summary>
    /// <c>reachFromAbove: DepotFunctions.BaysWhereIHold</c>: a resource is reached from above, by a function the
    /// application declares, beside a place of its own.
    /// </summary>
    /// <param name="function">The function that answers where the caller holds a key, one the application declares once.</param>
    public static implicit operator ReachFromAbove(DatabaseFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);

        return new ReachFromAbove(function);
    }
}
