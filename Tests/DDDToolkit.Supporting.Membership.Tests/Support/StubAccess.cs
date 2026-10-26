using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.Tests.Support;

/// <summary>
/// The access questions of one kind of resource, answered from what a test says the caller sees and holds: the
/// part a storage answers. What the package decides over it, the refusals and their order, is what the tests
/// are about.
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
/// <param name="rules">The rules of the resource the questions are about.</param>
public sealed class StubAccess<TResourceId>(MembershipRules rules) : IMemberQuestions<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    private readonly Dictionary<TResourceId, (long Version, Dictionary<string, MemberVia> Held)> _seen = [];

    /// <summary>What was asked, in order: <c>caller</c>, or <c>hold</c> and the key.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>The refusal a caller that is nobody is given, or <see langword="null"/> when the caller is somebody.</summary>
    public RefusalException? Nobody { get; set; }

    /// <summary>The moment the reaches are for.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Whether the caller is the application's own work.</summary>
    public bool System { get; set; }

    /// <summary>Until when the caller holds the keys it holds, or <see langword="null"/> when its holds have no end.</summary>
    public DateTimeOffset? Until { get; set; }

    /// <inheritdoc />
    public MembershipRules Rules { get; } = rules;

    /// <summary>The caller sees <paramref name="resource"/>, at <paramref name="version"/>, and holds these keys on it.</summary>
    public StubAccess<TResourceId> Sees(TResourceId resource, long version, params (string Key, MemberVia Via)[] held)
    {
        _seen[resource] = (version, held.ToDictionary(pair => pair.Key, pair => pair.Via, StringComparer.Ordinal));
        return this;
    }

    /// <inheritdoc />
    public void RequireCaller()
    {
        Asked.Add("caller");
        if (Nobody is not null)
        {
            throw Nobody;
        }
    }

    /// <inheritdoc />
    public MemberReach<TResourceId> Reach(string key) => new StubReach(key, Now, System);

    /// <inheritdoc />
    public MemberKeyReach<TResourceId> KeyReach(IReadOnlyCollection<string> keys)
        => new StubKeyReach(Rules.SeeKey is { } see ? Reach(see) : new StubReach(Now, System), [.. keys.Distinct(StringComparer.Ordinal)]);

    /// <inheritdoc />
    public Task<MemberHold<TResourceId>?> HoldAsync(TResourceId resource, string key, CancellationToken cancellationToken)
    {
        Asked.Add("hold " + key);
        return Task.FromResult(_seen.TryGetValue(resource, out var seen)
            ? seen.Held.TryGetValue(key, out var via)
                ? new MemberHold<TResourceId>(resource, via, seen.Version, Until)
                : new MemberHold<TResourceId>(resource, null, seen.Version, null)
            : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> KeysOnAsync(
        IReadOnlyCollection<TResourceId> resources,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<TResourceId, IReadOnlySet<string>> held = resources.Distinct()
            .Where(_seen.ContainsKey)
            .Select(resource => (resource, Keys: (IReadOnlySet<string>)_seen[resource].Held.Keys.Intersect(keys).ToHashSet(StringComparer.Ordinal)))
            .Where(pair => pair.Keys.Count > 0)
            .ToDictionary(pair => pair.resource, pair => pair.Keys);

        return Task.FromResult(held);
    }

    private sealed class StubReach : MemberReach<TResourceId>
    {
        public StubReach(string key, DateTimeOffset now, bool everything)
            : base(key, now, everything)
        {
        }

        /// <summary>The reach of what the caller sees, under rules that name no key for seeing.</summary>
        public StubReach(DateTimeOffset now, bool everything)
            : base(now, everything)
        {
        }
    }

    private sealed class StubKeyReach(MemberReach<TResourceId> see, IReadOnlyList<string> keys) : MemberKeyReach<TResourceId>(see, keys);
}
