using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// What the access check of the request in hand read of one resource, for the managers to reuse rather than ask again:
/// whether the check let the caller through with a permission on the resource, and whether it read the resource at all.
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
internal static class CheckedInHand<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>
    /// Whether the request in hand was checked for <paramref name="permission"/> on <paramref name="resource"/>, and the
    /// check found it held there. Only where every case of its requirement about the resource names that permission: a
    /// hold kept for the resource then answers about it, and about no other.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="permission">The permission the manager requires on it.</param>
    public static bool Holds(TResourceId resource, string permission)
    {
        if (RequestInHand.Current is not { } hand)
        {
            return false;
        }

        var permissions = new List<string>();
        Collect(hand.Requirement, resource, permissions);
        return permissions.Count > 0
               && permissions.All(each => string.Equals(each, permission, StringComparison.Ordinal))
               && Checked<MemberHold<TResourceId>>.TryFindInHand(hold => hold.Resource.Equals(resource) && hold.Via is not null, out _);
    }

    /// <summary>Whether the check of the request in hand read <paramref name="resource"/>: the caller sees it.</summary>
    /// <param name="resource">The resource.</param>
    public static bool Read(TResourceId resource)
        => Checked<MemberHold<TResourceId>>.TryFindInHand(hold => hold.Resource.Equals(resource), out _);

    /// <summary>The permissions the cases of <paramref name="requirement"/> about <paramref name="resource"/> name.</summary>
    private static void Collect(AccessRequirement requirement, TResourceId resource, List<string> permissions)
    {
        switch (requirement)
        {
            case AccessRequirement.Combined combined:
                foreach (var each in combined.Cases)
                {
                    Collect(each, resource, permissions);
                }

                break;

            case MemberAccess<TResourceId>.On on when on.Resource.Equals(resource):
                permissions.Add(on.Permission);
                break;

            case MemberAccess<TResourceId>.Sees sees when sees.Resource.Equals(resource):
                permissions.Add(sees.Permission);
                break;
        }
    }
}
