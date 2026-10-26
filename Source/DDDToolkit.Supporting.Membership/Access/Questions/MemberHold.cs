using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// What the access questions read of one resource the caller may see: how the caller holds the permission asked
/// about and until when, and the version the resource was at.
/// </summary>
/// <remarks>
/// <see cref="MemberAccessCheck{TResource, TResourceId}"/> keeps the one it read for a request, in
/// <see cref="DDDToolkit.Access.Checked{T}"/>, for the expert hold: a context that asks for it holds every save
/// of the resource to <see cref="Version"/>. A handler on the default path takes nothing from there. A rule of
/// its own that reads <see cref="Via"/> or <see cref="Until"/> asks the questions for a hold
/// (<see cref="IMemberQuestions{TResourceId}.HoldAsync"/>).
/// </remarks>
/// <param name="Resource">The resource that was read.</param>
/// <param name="Via">
/// How the caller holds the permission on it, or <see langword="null"/> when the caller sees the resource without
/// holding the permission. So never <see langword="null"/> on a hold the permission's requirement let through
/// (<see cref="MemberAccess{TResourceId}.On"/>), and <see langword="null"/> on one the caller's own way through let
/// through (<see cref="MemberAccess{TResourceId}.Sees"/>) where the caller does not hold the permission as well.
/// </param>
/// <param name="Version">
/// The resource's version when it was read. Under the expert hold a save of the resource at another version is
/// a lost race, so what is changed is what was checked.
/// </param>
/// <param name="Until">
/// The first moment the caller no longer holds the permission on the resource, as its membership and its roles stand
/// now, or <see langword="null"/> when the hold has no end: so for the application's own work, and for a permission
/// the resource's owner holds by owning it, the permissions marked <c>Owner</c>
/// (<see cref="MembershipRules.OwnerPermissions"/>). The owner holds no other permission by owning, and holds no role. A member holds a permission that being a member gives until its
/// membership ends, and a permission a role gives until the last of its roles that give the permission now ends, or its
/// membership if that ends sooner; a role that only starts later does not lengthen it.
/// <see langword="null"/> as well when the permission is not held (<paramref name="Via"/> is <see langword="null"/>).
/// <para>
/// A permission held from above has no end the package knows: it is held where the application says, and the
/// application knows until when. It is held on a resource for as long as the caller sees that resource. So
/// it is <see langword="null"/> where the caller sees the resource from above as well, by holding the permission
/// that sees there, whatever its membership and its roles say; and where the caller sees the resource as a
/// member alone, it is the end of that membership, since the resource is not there for it after.
/// </para>
/// <para>
/// The package refuses nothing by it. It is there for a rule of the application's own, such as that nobody
/// gives a role for longer than they hold the permission they give it with.
/// </para>
/// </param>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public sealed record MemberHold<TResourceId>(TResourceId Resource, MemberVia? Via, long Version, DateTimeOffset? Until)
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>;
