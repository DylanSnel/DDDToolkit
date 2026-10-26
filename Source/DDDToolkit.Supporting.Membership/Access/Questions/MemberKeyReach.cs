using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Which of several keys the caller holds on which resources of one kind, at one moment:
/// <see cref="MemberReach{TResourceId}"/> for many keys at once, for a list that shows what its caller may do
/// with each row without a statement per row and key.
/// <para>
/// Made by <see cref="IMemberQuestions{TResourceId}.KeyReach"/>, and put into a query by the storage those
/// questions read through, as with a reach and for the same reason. A key on a resource the caller may not see
/// is never answered, so asking about a resource says nothing about whether it exists.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public abstract class MemberKeyReach<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>For the access questions of a storage.</summary>
    /// <param name="see">The reach that decides which resources the caller sees: only those are answered about.</param>
    /// <param name="keys">The keys asked about, each once.</param>
    protected MemberKeyReach(MemberReach<TResourceId> see, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(see);
        ArgumentNullException.ThrowIfNull(keys);

        See = see;
        Keys = keys;
    }

    /// <summary>The reach that decides which resources the caller sees: only those are answered about.</summary>
    public MemberReach<TResourceId> See { get; }

    /// <summary>The keys asked about, each once. The application's own work holds them all on every resource.</summary>
    public IReadOnlyList<string> Keys { get; }
}
