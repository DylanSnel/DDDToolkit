namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Says that a resource is reached from above as well as through its members: it sits at a place, a unit of
/// an organization say, and a caller that holds a key at that place, or above it, holds the key on the
/// resource without being a member of it.
/// <code>
/// new MembershipRules("crates", keys: ["crates.see", "crates.scrap"], roles: [...], seeKey: "crates.see",
///     above: new("depot/bays_where_i_hold"))
/// </code>
/// <para>
/// Where a resource sits is said where its members are mapped, and where a caller holds a key is the
/// application's to answer: in C# through a port it registers with the resource, which the package that
/// stores the resources asks inside its own statement, and in a database that answers the questions itself
/// through a function of the application's, named here. The package keeps no places and no tree of them.
/// </para>
/// <para>
/// A resource the caller does not see does not exist for it, whatever it holds there. So rules that let a
/// resource be reached from above name the key that sees it (<see cref="MembershipRules.SeeKey"/>): whoever
/// holds that key where the resource sits sees the resource, as its members do, and without such a key
/// nobody above would.
/// </para>
/// <para>
/// A key held from above is any key the application answers for: it is not cut by
/// <see cref="MembershipRules.MemberKeys"/>, which is about what a member's role gives. A caller that holds a
/// key both ways holds it as a member (<see cref="MemberVia.Members"/>).
/// </para>
/// </summary>
public sealed record ReachFromAbove
{
    /// <summary>Reach from above.</summary>
    /// <param name="function">
    /// The function that answers where the caller holds a key in the database, by its logical name,
    /// <c>owner/name</c>: one that takes the key as text and answers the places the caller's hold of it
    /// reaches, each place it is held at and every place below, as the resource's row stores where it sits.
    /// Left out by an application whose database does not answer the questions itself; whatever writes the
    /// resource's functions refuses rules that name none. It is asked by the resource's functions, which
    /// run as their owner, so it reads every row whatever policies its tables have: it answers for the
    /// caller by a condition of its own, never by a policy. It is never asked without a key.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="function"/> is not a logical name, <c>owner/name</c>.</exception>
    public ReachFromAbove(string? function = null) => Function = MemberSource.LogicalName(function, nameof(function));

    /// <summary>
    /// The logical name of the function that answers where the caller holds a key in the database, or
    /// <see langword="null"/> when the rules name none.
    /// </summary>
    public string? Function { get; }
}
