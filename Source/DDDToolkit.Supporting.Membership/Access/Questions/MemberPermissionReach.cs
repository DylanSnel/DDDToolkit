using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which of several permissions the caller holds on which resources of one kind, at one moment:
/// <see cref="MemberReach{TResourceId}"/> for many permissions at once, for a list that shows what its caller may do
/// with each row without a statement per row and permission.
/// <para>
/// Made by <see cref="IMemberQuestions{TResourceId}.PermissionReach"/>, and put into a query by the storage those
/// questions read through, as with a reach and for the same reason. A permission on a resource the caller may not see
/// is never answered, so asking about a resource says nothing about whether it exists.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public abstract class MemberPermissionReach<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>For the access questions of a storage.</summary>
    /// <param name="see">The reach that decides which resources the caller sees: only those are answered about.</param>
    /// <param name="permissions">The permissions asked about, each once.</param>
    protected MemberPermissionReach(MemberReach<TResourceId> see, IReadOnlyList<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(see);
        ArgumentNullException.ThrowIfNull(permissions);

        See = see;
        Permissions = permissions;
    }

    /// <summary>The reach that decides which resources the caller sees: only those are answered about.</summary>
    public MemberReach<TResourceId> See { get; }

    /// <summary>The permissions asked about, each once. The application's own work holds them all on every resource.</summary>
    public IReadOnlyList<string> Permissions { get; }
}
