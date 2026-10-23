using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Says on a query that it shows the caller only the resources it holds <paramref name="key"/> on: what
/// <see cref="MemberAccess.SeenWith{TResourceId}"/> says, written where the query is declared.
/// <code>
/// [SeenWith&lt;DocumentId&gt;(DocumentKeys.View)]
/// public sealed record ListDocuments : IQuery&lt;IReadOnlyList&lt;DocumentListing&gt;&gt;, IDocumentsRequest;
/// </code>
/// is <c>MemberAccess.SeenWith&lt;DocumentId&gt;(DocumentKeys.View)</c>.
/// </summary>
/// <remarks>
/// For queries only. It refuses nobody for want of the key: it lets a signed-in caller through, one that holds
/// nothing too, and says what the query's own statement filters by, which is the check. On a message of the Mediator
/// library that is no query, a command or a plain request, it would let the handler run unchecked, so there it is
/// DDD00082.
/// </remarks>
/// <typeparam name="TResourceId">The id of the kind of resource the query is about.</typeparam>
/// <param name="key">The key that decides what the query shows: a constant, one of the resource's rules.</param>
[StatesRequirement(typeof(MemberAccess), nameof(MemberAccess.SeenWith), QueriesOnly = true)]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SeenWithAttribute<TResourceId>(string key) : Attribute
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>The key that decides what the query shows.</summary>
    public string Key { get; } = key;
}
