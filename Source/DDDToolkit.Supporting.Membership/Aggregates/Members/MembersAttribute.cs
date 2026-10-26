namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// Names a resource with members otherwise than after its class: what its codes and its functions in the database
/// are named after.
/// <code>
/// [Members("Whiteboards")]
/// [AggregateRoot&lt;BoardId&gt;]
/// public sealed partial class Board;
/// </code>
/// Without it a resource is named after its class, the plural of its name in lower case: <c>Board</c> is
/// <c>boards</c>, <c>CraneBoard</c> <c>crane-boards</c>, <c>Category</c> <c>categories</c>, <c>Box</c> <c>boxes</c>.
/// The name is what the codes a client reads start with, <c>boards.already-member</c>, and what the functions in
/// the database are named after, <c>boards_i_see</c>, with an underscore where the codes have a dash.
/// </summary>
/// <remarks>
/// Say it where the plural is not the class's (<c>[Members("People")]</c> on a class <c>Person</c>), and after a
/// class is renamed, to keep the name its codes and its functions already have: a new name changes the codes
/// clients read and gives the database new functions, which the build's export warns of (DDD00090). The name is
/// written as a class would be, <c>People</c> or <c>CraneBoards</c>, or as it comes out, <c>crane-boards</c>; it is
/// converted the same way. Letters and digits only, with <c>-</c> or <c>_</c> between words; a space, any other
/// symbol, or a digit first is DDD00091.
/// </remarks>
/// <param name="name">The resource's name, such as <c>People</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MembersAttribute(string name) : Attribute
{
    /// <summary>The resource's name, as it was written.</summary>
    public string Name { get; } = name;
}
