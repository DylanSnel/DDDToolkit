using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// What the access questions read of one resource the caller may see: how the caller holds the key asked
/// about and until when, and what a command needs to act on exactly the resource that was checked.
/// </summary>
/// <param name="Resource">The resource that was read: the one a command then loads, whatever its request says.</param>
/// <param name="Via">
/// How the caller holds the key on it, or <see langword="null"/> when the caller sees the resource without
/// holding the key. Never <see langword="null"/> on a hold a requirement let through.
/// </param>
/// <param name="Version">
/// The resource's version when it was read. A command loads the resource at this version, so what it changes
/// is what was checked, and a resource changed in between is a lost race.
/// </param>
/// <param name="Until">
/// The first moment the caller no longer holds the key on the resource, as its membership and its roles stand
/// now, or <see langword="null"/> when the hold has no end: so for the application's own work, and for a key
/// the resource's owner holds by owning it, which is every key the rules state
/// (<see cref="MembershipRules.Keys"/>). A key the rules do not state the owner holds as any member does,
/// through a role, and until that role ends. A member holds a key that being a member gives until its
/// membership ends, and a key a role gives until the last of its roles that give the key now ends, or its
/// membership if that ends sooner; a role that only starts later does not lengthen it.
/// <see langword="null"/> as well when the key is not held (<paramref name="Via"/> is <see langword="null"/>).
/// <para>
/// A key held from above has no end the package knows: it is held where the application says, and the
/// application knows until when. It is held on a resource for as long as the caller sees that resource. So
/// it is <see langword="null"/> where the caller sees the resource from above as well, by holding the key
/// that sees there, whatever its membership and its roles say; and where the caller sees the resource as a
/// member alone, it is the end of that membership, since the resource is not there for it after.
/// </para>
/// <para>
/// The package refuses nothing by it. It is there for a rule of the application's own, such as that nobody
/// gives a role for longer than they hold the key they give it with.
/// </para>
/// </param>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public sealed record MemberHold<TResourceId>(TResourceId Resource, MemberVia? Via, long Version, DateTimeOffset? Until)
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>;
