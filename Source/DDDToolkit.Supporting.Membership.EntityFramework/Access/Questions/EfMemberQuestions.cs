using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The access questions of one kind of resource, answered from the context that maps it:
/// <see cref="IMemberQuestions{TResourceId}"/> for a resource stored with Entity Framework.
/// </summary>
/// <remarks>
/// <para>
/// Every question is asked for the caller that is current when it is asked, and at one moment: the reach that
/// sees and the reach that acts of one question share it. Who the caller is as a member comes from the rules,
/// its user id, a claim of its token, or what the application resolves for it, and only a signed-in user is
/// one. The owner of a resource is found by the resource's own owner column, holds every key of the
/// resource whatever its roles give, and sees it. Which roles give a key is what the rules declare; where the roles are
/// kept for the resource, what their rows say, read inside the statement; or, where they are kept elsewhere,
/// what the application answers inside the statement. Either way only for a key a member's role can give
/// at all. Where the rules let a resource be reached from above, a signed-in caller holds a key on it as well
/// when the application answers that it holds the key where the resource sits. The application itself
/// (<see cref="Caller.System"/>) holds every key on every resource, and so does its work in a scope the rules
/// name as its own for the resource (<see cref="MembershipRules.SystemScopes"/>). Work in any other scope is
/// nobody's member and nothing above the rules: like anybody else that holds a key in none of these ways it
/// reaches nothing, and nothing is read for it.
/// </para>
/// <para>
/// A question that reads runs one statement, on a context of its own from the context's factory where the
/// application registered one, since the questions of one request may be asked side by side; otherwise on the
/// request's own context. Where the application's rule about the rows a question reads, the resource's or
/// those of the roles kept for it, is a query filter that reads a member of the context it runs on, the question runs on
/// the request's own context wherever the scope has one, as what the admission asks does: such a rule is
/// that context's, and a context the factory makes knows nothing of it. So a role the admission would refuse
/// gives nothing where the questions read either. A filter that reads what is around the work, and not the
/// context, holds on every context, and takes nothing from a reading on a context of its own.
/// </para>
/// </remarks>
internal sealed class EfMemberQuestions<TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId> : IMemberQuestions<TResourceId>
    where TContext : DbContext
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly IServiceProvider _services;
    private readonly IDbContextFactory<TContext>? _contexts;
    private readonly MembershipRegistration _registration;
    private readonly MemberNamesOf<TContext, TResource, TRoleId> _names;
    private readonly Func<Caller, TMemberId?> _asMember;
    private readonly ICallerMember<TResourceId, TMemberId>? _resolved;
    private readonly IRolesWithKey<TResourceId, TRoleId>? _kept;
    private readonly ICallerAccessor _callers;
    private readonly TimeProvider _clock;

    /// <summary>The access questions of one kind of resource.</summary>
    /// <param name="registration">The resource, with its rules.</param>
    /// <param name="asMember">
    /// Who a caller is as a member, as the rules say, or nobody; <see langword="null"/> where the application
    /// resolves it, and is asked.
    /// </param>
    /// <param name="services">The services of the scope the questions are asked in.</param>
    public EfMemberQuestions(MembershipRegistration registration, Func<Caller, TMemberId?>? asMember, IServiceProvider services)
    {
        var rules = registration.Rules;
        Rules = rules;
        _registration = registration;
        _services = services;
        _contexts = services.GetService<IDbContextFactory<TContext>>();
        _names = services.GetRequiredService<MemberNamesOf<TContext, TResource, TRoleId>>();
        _callers = services.GetRequiredService<ICallerAccessor>();
        _clock = services.GetRequiredService<TimeProvider>();

        // What the application answers is asked of the scope the questions are asked in: it knows the request.
        _resolved = asMember is null ? services.GetRequiredService<ICallerMember<TResourceId, TMemberId>>() : null;
        _asMember = asMember ?? _resolved!.Find;
        _kept = rules.RolesKeptElsewhere is null ? null : services.GetRequiredService<IRolesWithKey<TResourceId, TRoleId>>();
    }

    /// <inheritdoc />
    public MembershipRules Rules { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Nobody is refused here where a caller's member id is its own id or a claim: a caller that is nobody's
    /// member simply reaches nothing. Where the application resolves it, the application says who it refuses
    /// outright, about every caller that is not its own work for this resource: work in a scope the rules do
    /// not name is asked about like anybody.
    /// </remarks>
    public void RequireCaller()
    {
        var caller = _callers.Current;
        if (_resolved is not null && !Rules.IsOwnWork(caller))
        {
            _resolved.Require(caller);
        }
    }

    /// <inheritdoc />
    public MemberReach<TResourceId> Reach(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return ReachFor(key, Ask());
    }

    /// <inheritdoc />
    public MemberKeyReach<TResourceId> KeyReach(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return KeyReachFor(Distinct(keys), Ask());
    }

    /// <inheritdoc />
    public async Task<MemberHold<TResourceId>?> HoldAsync(TResourceId resource, string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var asked = Ask();
        var see = See(asked);
        if (see.Nothing)
        {
            return null;
        }

        // Whether it is there for the caller is the filter; how the caller holds the key on it, and until
        // when, is what is read. A key held from above is held for as long as the resource is seen.
        var act = ReachFor(key, asked);
        var held = await ReadAsync(
            context => act.HeldWhereSeen(context.Set<TResource>().AsNoTracking().Where(candidate => candidate.Id.Equals(resource)), context, see)
                .Select(found => new { found.Resource.Version, found.AsMember, found.FromAbove, found.Until })
                .FirstOrDefaultAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return held is null
            ? null
            : new MemberHold<TResourceId>(resource, act.Via(held.AsMember, held.FromAbove), held.Version, held.Until);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> KeysOnAsync(
        IReadOnlyCollection<TResourceId> resources,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(keys);

        var about = resources.Distinct().ToList();
        if (about.Count > MemberQuestions.MostResources)
        {
            throw new ArgumentException(
                "Which keys are held is asked about at most " + MemberQuestions.MostResources + " different resources at once, and these are "
                + about.Count + ". A use case that takes the ids from a request refuses its caller before it asks.",
                nameof(resources));
        }

        var wanted = Distinct(keys);
        var asked = Ask();
        var reach = KeyReachFor(wanted, asked);
        var none = new Dictionary<TResourceId, IReadOnlySet<string>>();
        if (about.Count == 0 || wanted.Count == 0 || reach.HoldsNothing)
        {
            return none;
        }

        if (asked.Everything)
        {
            // The application's own work for this resource holds every key on every one, so only which exist is read.
            var existing = await ReadAsync(
                context => context.Set<TResource>().AsNoTracking().Where(candidate => about.Contains(candidate.Id)).Select(candidate => candidate.Id).ToListAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return existing.ToDictionary(id => id, _ => (IReadOnlySet<string>)wanted.ToHashSet(StringComparer.Ordinal));
        }

        var held = await ReadAsync(
            context => reach.KeysOn(context.Set<TResource>().AsNoTracking().Where(candidate => about.Contains(candidate.Id)), context).ToListAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // One row for each key held on a resource, put together by resource here.
        return held
            .GroupBy(pair => pair.Resource)
            .ToDictionary(pairs => pairs.Key, pairs => (IReadOnlySet<string>)pairs.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>The keys asked about, each once, in the order they were asked.</summary>
    private static List<string> Distinct(IReadOnlyCollection<string> keys)
        => keys.Any(string.IsNullOrWhiteSpace)
            ? throw new ArgumentException("A permission key is not blank.", nameof(keys))
            : [.. keys.Distinct(StringComparer.Ordinal)];

    /// <summary>Who asks, and when: taken once for a question, so everything it is answered from is of one caller and one moment.</summary>
    private Asking Ask()
    {
        var caller = _callers.Current;
        var signedIn = caller.Kind == CallerKind.User;
        return new Asking(
            caller,

            // The application itself, and its work in a scope the rules name as its own for this resource.
            // Work in any other scope is not above the rules, and is no signed-in user either: it holds nothing.
            Rules.IsOwnWork(caller),
            signedIn,
            signedIn ? _asMember(caller) : null,
            _clock.GetUtcNow());
    }

    /// <summary>
    /// The reach of what the caller sees: the resources it is a member of now, whatever roles it holds there;
    /// those it owns, by the owner column, since the owner holds every key of a resource by owning it, and
    /// does so from the moment the resource names it, before its own membership has begun; and, where the
    /// rules let a resource be reached from above and name a key for seeing, those that sit where the caller
    /// holds that key. The functions in the database see a resource the same way.
    /// </summary>
    private EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> See(Asking asked)
    {
        var names = Names();
        var heldBy = asked.Member is null ? MemberHeldBy.None : MemberHeldBy.Membership | MemberHeldBy.Owner;
        var member = asked.Member ?? default;
        if (Rules.SeeKey is not { } key)
        {
            // No key to ask about above: such a resource is seen by its members and its owner, and by nobody else.
            return new(asked.Now, asked.Everything, heldBy, member, names);
        }

        var above = asked.SignedIn && !asked.Everything ? names.Above?.Reaching(_services, asked.Caller, key) : null;
        return new(key, asked.Now, asked.Everything, above is null ? heldBy : heldBy | MemberHeldBy.Above, member, [], names, Asked(null, above));
    }

    /// <summary>
    /// The reach for one key, as the rules say it is held: by the owner, for a key of the resource; by being a
    /// member, for the one key that gives; through the roles that give it; and from above, where the rules
    /// say a resource is reached so. A key held in none of these ways reaches nothing.
    /// </summary>
    private EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> ReachFor(string key, Asking asked)
    {
        var names = Names();
        if (asked.Everything || !asked.SignedIn)
        {
            return new(key, asked.Now, asked.Everything, MemberHeldBy.None, default, [], names);
        }

        var heldBy = MemberHeldBy.None;
        List<TRoleId> roles = [];
        Func<DbContext, IQueryable<TRoleId>>? kept = null;
        if (asked.Member is not null)
        {
            // The owner holds every key of the resource by owning it, whatever its roles give.
            if (Rules.OwnerHolds(key))
            {
                heldBy |= MemberHeldBy.Owner;
            }

            if (Rules.MembershipGives(key))
            {
                heldBy |= MemberHeldBy.Membership;
            }
            else if (Rules.RolesKept)
            {
                // The roles are rows kept for the resource, and which of them give the key is read from
                // those rows, inside the statement. Only for a key a member's role can give: whatever a
                // row still holds from before the rules changed, it gives no key the rules keep from members.
                if (Rules.MemberKeys.Allows(key))
                {
                    kept = names.Roles!.ThatGive(key);
                    heldBy |= MemberHeldBy.Roles;
                }
            }
            else if (_kept is null)
            {
                // The roles are the ones the rules declare, held under their names: checked where the resource was registered.
                roles = [.. Rules.RolesWith(key).Select(role => (TRoleId)(object)role)];
                if (roles.Count > 0)
                {
                    heldBy |= MemberHeldBy.Roles;
                }
            }
            else if (Rules.MemberKeys.Allows(key))
            {
                // The roles are kept elsewhere, and which of them give the key is asked there, inside the
                // statement. Only for a key a member's role can give: whatever else such a role holds, it
                // gives no key the rules keep from members.
                var answers = _kept;
                var caller = asked.Caller;
                kept = context => answers.RolesWith(context, caller, key)
                    ?? throw new InvalidOperationException(answers.GetType().Name + " answered no query for the roles that give '" + key + "'. Answer an empty one for none.");
                heldBy |= MemberHeldBy.Roles;
            }
        }

        // Held where the resource sits, or above it: asked of the application, for members and others alike.
        var above = names.Above?.Reaching(_services, asked.Caller, key);
        if (above is not null)
        {
            heldBy |= MemberHeldBy.Above;
        }

        return new(key, asked.Now, false, heldBy, asked.Member ?? default, roles, names, Asked(kept, above));
    }

    /// <summary>What a reach puts into a statement as a query of its own, or <see langword="null"/> when it puts in none.</summary>
    private AskedOfTheApplication<TResource, TRoleId>? Asked(Func<DbContext, IQueryable<TRoleId>>? roles, Func<DbContext, System.Linq.Expressions.Expression<Func<TResource, bool>>>? above)
        => roles is null && above is null ? null : new(roles, above, () => _services.GetService<TContext>());

    private EfMemberKeyReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> KeyReachFor(List<string> keys, Asking asked)
        => new(See(asked), [.. keys.Select(key => ReachFor(key, asked))]);

    /// <summary>
    /// Runs one reading: on a context of its own where the application registered a factory for the context,
    /// disposed when the reading is done, and otherwise on the context of the scope. Where the rows it reads
    /// are under a query filter that reads its context, on the context of the scope wherever it has one: that
    /// rule is the request's context's, as it is for what the admission asks.
    /// </summary>
    private async Task<T> ReadAsync<T>(Func<TContext, Task<T>> read, CancellationToken cancellationToken)
    {
        if (_contexts is null)
        {
            return await read(_services.GetRequiredService<TContext>()).ConfigureAwait(false);
        }

        if (Names().FollowTheRequest && _services.GetService<TContext>() is { } own)
        {
            return await read(own).ConfigureAwait(false);
        }

        var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await read(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The names of the resource's members and of its owner, where it sits, and the rows of its roles where
    /// they are kept, read from the context's model the first time they are needed.
    /// </summary>
    private MemberNames<TResource, TRoleId> Names() => _names.Read(_services, _registration);

    /// <summary>Who asks, and when.</summary>
    /// <param name="Caller">The caller the question is asked for.</param>
    /// <param name="Everything">Whether the caller is the application's own work for this resource.</param>
    /// <param name="SignedIn">Whether the caller is a signed-in user: the only caller that is a member, or holds a key from above.</param>
    /// <param name="Member">Who the caller is as a member, or nobody.</param>
    /// <param name="Now">The moment the question is asked at.</param>
    private readonly record struct Asking(Caller Caller, bool Everything, bool SignedIn, TMemberId? Member, DateTimeOffset Now);
}
