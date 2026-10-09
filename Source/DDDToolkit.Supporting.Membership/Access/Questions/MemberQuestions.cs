using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The questions that refuse, asked over what a storage answers
/// (<see cref="IMemberQuestions{TResourceId}.HoldAsync"/>). They are written once, here, so that what a caller is
/// refused with, and in which order, is the same whichever storage keeps the resources.
/// <para>
/// The order is the same for every question: the caller's own refusal when it is nobody the questions can be
/// asked about; then <c>not-found</c> for a resource it may not see, exactly as for one that does not exist,
/// so nobody learns a resource is there; then <c>not-permitted</c>, naming the key, for a resource it sees
/// without holding the key. All under the codes of the resource asked about.
/// </para>
/// </summary>
public static class MemberQuestions
{
    /// <summary>The most resources one question about several is asked about.</summary>
    public const int MostResources = 200;

    /// <summary>
    /// Requires the caller to hold <paramref name="key"/> on a resource: what a command or a query about one
    /// resource passes before its handler. One statement.
    /// </summary>
    /// <param name="access">The access questions of the resource's kind.</param>
    /// <param name="resource">The resource.</param>
    /// <param name="key">The key the request needs on it.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>What was read of the resource, with the version it was checked at and how the key is held.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>not-found</c> when it may not see the resource, for a
    /// missing one too; <c>not-permitted</c>, with the <c>Key</c>, when it sees the resource but does not hold
    /// the key on it.
    /// </exception>
    public static async Task<MemberHold<TResourceId>> RequireAsync<TResourceId>(
        this IMemberQuestions<TResourceId> access,
        TResourceId resource,
        string key,
        CancellationToken cancellationToken)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // The caller before anything is read: nobody is refused as nobody, without a statement made for it.
        access.RequireCaller();

        var codes = access.Rules.Codes;
        var hold = await access.HoldAsync(resource, key, cancellationToken).ConfigureAwait(false)
                   ?? throw codes.Refuse(MembershipRefusals.NotFound);

        return hold.Via is null
            ? throw codes.Refuse(MembershipRefusals.NotPermitted, ("Key", key))
            : hold;
    }

    /// <summary>
    /// How the caller holds <paramref name="key"/> on a resource it may see, or <see langword="null"/> when it
    /// does not hold it: for a query that answers with what its caller may do, where not holding the key is an
    /// answer and not a refusal. One statement.
    /// </summary>
    /// <param name="access">The access questions of the resource's kind.</param>
    /// <param name="resource">The resource.</param>
    /// <param name="key">A permission key.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>not-found</c> when the caller may not see the resource.
    /// </exception>
    public static async Task<MemberVia?> ViaAsync<TResourceId>(
        this IMemberQuestions<TResourceId> access,
        TResourceId resource,
        string key,
        CancellationToken cancellationToken)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // The same first step as every question: nobody is told a resource is not found, as if it had been
        // looked for, when the questions cannot be asked about that caller at all.
        access.RequireCaller();

        var hold = await access.HoldAsync(resource, key, cancellationToken).ConfigureAwait(false)
                   ?? throw access.Rules.Codes.Refuse(MembershipRefusals.NotFound);

        return hold.Via;
    }
}
