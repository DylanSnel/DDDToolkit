using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The access questions of one kind of resource, answered from the context that maps it: what the access manager of a
/// resource stored with Entity Framework reads through (<see cref="IMembershipStorage{TResource, TResourceId}"/>), and the
/// queries on the resource's own set (<c>db.Boards.WhereIHold(permission)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every question is asked for the user that is current when it is asked, and at one moment: the reach that
/// sees and the reach that acts of one question share it. Who the user is as a member comes from the rules,
/// its user id, a claim of its token, or what the application resolves for it, and only a signed-in user is
/// one. The owner of a resource is found by the resource's own owner column, holds the permissions the rules give
/// an owner, holding no role, and sees it. Which roles give a permission is what the rules declare; where the roles are
/// kept for the resource, what their rows say, read inside the statement; or, where they are kept elsewhere,
/// what the application answers inside the statement. Either way only for a permission a member's role can give
/// at all. Where the rules let a resource be reached from above, a signed-in user holds a permission on it as well
/// when the application answers that it holds the permission where the resource sits. The application itself
/// (<see cref="CurrentUserSnapshot.System"/>) holds every permission on every resource, and so does its work in a scope the rules
/// name as its own for the resource (<see cref="MembershipRules.SystemScopes"/>). Work in any other scope is
/// nobody's member and nothing above the rules: like anybody else that holds a permission in none of these ways it
/// reaches nothing, and nothing is read for it.
/// </para>
/// <para>
/// A question that reads runs one statement, on a context of its own from the context's factory where the
/// application registered one, since the questions of one request may be asked side by side; otherwise on the
/// request's own context. Where the application's rule about the rows a question reads, the resource's or
/// those of the roles kept for it, is a query filter that reads a member of the context it runs on, the question runs on
/// the request's own context wherever the scope has one, as what the member manager asks does: such a rule is
/// that context's, and a context the factory makes knows nothing of it. So a role the member manager would refuse
/// gives nothing where the questions read either. A filter that reads what is around the work, and not the
/// context, holds on every context, and takes nothing from a reading on a context of its own.
/// </para>
/// </remarks>
internal sealed class EfMemberQuestions<TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId> : IMembershipStorage<TResource, TResourceId>, IMemberQueries<TResource>
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
    private readonly Func<CurrentUserSnapshot, TMemberId?> _asMember;
    private readonly ICurrentMember<TResourceId, TMemberId>? _resolved;
    private readonly IRolesWithPermission<TResourceId, TRoleId>? _kept;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    /// <summary>The access questions of one kind of resource.</summary>
    /// <param name="registration">The resource, with its rules.</param>
    /// <param name="asMember">
    /// Who a user is as a member, as the rules say, or nobody; <see langword="null"/> where the application
    /// resolves it, and is asked.
    /// </param>
    /// <param name="services">The services of the scope the questions are asked in.</param>
    public EfMemberQuestions(MembershipRegistration registration, Func<CurrentUserSnapshot, TMemberId?>? asMember, IServiceProvider services)
    {
        var rules = registration.Rules;
        Rules = rules;
        _registration = registration;
        _services = services;
        _contexts = services.GetService<IDbContextFactory<TContext>>();
        _names = services.GetRequiredService<MemberNamesOf<TContext, TResource, TRoleId>>();
        _user = services.GetRequiredService<ICurrentUser>();
        _clock = services.GetRequiredService<TimeProvider>();

        // What the application answers is asked of the scope the questions are asked in: it knows the request.
        _resolved = asMember is null ? services.GetRequiredService<ICurrentMember<TResourceId, TMemberId>>() : null;
        _asMember = asMember ?? _resolved!.Find;
        _kept = rules.Roles.Kind == RolesKind.External ? services.GetRequiredService<IRolesWithPermission<TResourceId, TRoleId>>() : null;
    }

    /// <inheritdoc />
    public MembershipRules Rules { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Nobody is refused here where a user's member id is its own id or a claim: a user that is nobody's
    /// member simply reaches nothing. Where the application resolves it, the application says who it refuses
    /// outright, about every user that is not its own work for this resource: work in a scope the rules do
    /// not name is asked about like anybody.
    /// </remarks>
    public void RequireCurrentUser()
    {
        var user = _user.Snapshot;
        if (_resolved is not null && !Rules.IsOwnWork(user))
        {
            _resolved.Require(user);
        }
    }

    /// <inheritdoc />
    public MemberReach<TResource> Reach(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return ReachFor(permission, Ask());
    }

    /// <inheritdoc />
    public MemberReach<TResource> ReachSeen() => See(Ask());

    /// <inheritdoc />
    public IQueryable<TResource> WhereIHold(IQueryable<TResource> resources, string permission, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return ReachFor(permission, Ask()).Within(resources, context);
    }

    /// <inheritdoc />
    public IQueryable<PermissionOn<TResource>> WhereIHold(IQueryable<TResource> resources, IReadOnlyCollection<string> permissions, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(permissions);

        return PermissionReachFor(Distinct(permissions), Ask()).Pairs(resources, context);
    }

    /// <inheritdoc />
    public async Task<MemberHold<TResourceId>?> HoldAsync(TResourceId resource, string permission, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        var asked = Ask();
        var see = See(asked);
        if (see.Nothing)
        {
            return null;
        }

        // Whether it is there for the user is the filter; how the user holds the permission on it, and until
        // when, is what is read. A permission held from above is held for as long as the resource is seen.
        var act = ReachFor(permission, asked);
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
    public async Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> PermissionsOnAsync(
        IReadOnlyCollection<TResourceId> resources,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(permissions);

        var about = resources.Distinct().ToList();
        if (about.Count > MembershipAccessManager.MostResources)
        {
            throw new ArgumentException(
                "Which permissions are held is asked about at most " + MembershipAccessManager.MostResources + " different resources at once, and these are "
                + about.Count + ". A use case that takes the ids from a request refuses its user before it asks.",
                nameof(resources));
        }

        var wanted = Distinct(permissions);
        var asked = Ask();
        var reach = PermissionReachFor(wanted, asked);
        var none = new Dictionary<TResourceId, IReadOnlySet<string>>();
        if (about.Count == 0 || wanted.Count == 0 || reach.HoldsNothing)
        {
            return none;
        }

        if (asked.Everything)
        {
            // The application's own work for this resource holds every permission on every one, so only which exist is read.
            var existing = await ReadAsync(
                context => context.Set<TResource>().AsNoTracking().Where(candidate => about.Contains(candidate.Id)).Select(candidate => candidate.Id).ToListAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return existing.ToDictionary(id => id, _ => (IReadOnlySet<string>)wanted.ToHashSet(StringComparer.Ordinal));
        }

        var held = await ReadAsync(
            context => reach.PermissionsOn(context.Set<TResource>().AsNoTracking().Where(candidate => about.Contains(candidate.Id)), context).ToListAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // One row for each permission held on a resource, put together by resource here.
        return held
            .GroupBy(pair => pair.Resource)
            .ToDictionary(pairs => pairs.Key, pairs => (IReadOnlySet<string>)pairs.Select(pair => pair.Permission).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>The permissions asked about, each once, in the order they were asked.</summary>
    private static List<string> Distinct(IReadOnlyCollection<string> permissions)
        => permissions.Any(string.IsNullOrWhiteSpace)
            ? throw new ArgumentException("A permission is not blank.", nameof(permissions))
            : [.. permissions.Distinct(StringComparer.Ordinal)];

    /// <summary>Who asks, and when: taken once for a question, so everything it is answered from is of one user and one moment.</summary>
    private Asking Ask()
    {
        var user = _user.Snapshot;
        var signedIn = user.Kind == CurrentUserKind.User;
        return new Asking(
            user,

            // The application itself, and its work in a scope the rules name as its own for this resource.
            // Work in any other scope is not above the rules, and is no signed-in user either: it holds nothing.
            Rules.IsOwnWork(user),
            signedIn,
            signedIn ? _asMember(user) : null,
            _clock.GetUtcNow());
    }

    /// <summary>
    /// The reach of what the user sees: the resources it is a member of now, whatever roles it holds there;
    /// those it owns, by the owner column, since the owner sees what it owns, from the moment the resource names
    /// it, before its own membership has begun; and, where the
    /// rules let a resource be reached from above and name a permission for seeing, those that sit where the user
    /// holds that permission. The functions in the database see a resource the same way.
    /// </summary>
    private EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> See(Asking asked)
    {
        var names = Names();
        var heldBy = asked.Member is null ? MemberHeldBy.None : MemberHeldBy.Membership | MemberHeldBy.Owner;
        var member = asked.Member ?? default;
        if (Rules.EveryMemberPermission is not { } permission)
        {
            // No permission to ask about above: such a resource is seen by its members and its owner, and by nobody else.
            return new(asked.Now, asked.Everything, heldBy, member, names);
        }

        var above = asked.SignedIn && !asked.Everything ? names.Above?.Reaching(_services, asked.User, permission) : null;
        return new(permission, asked.Now, asked.Everything, above is null ? heldBy : heldBy | MemberHeldBy.Above, member, [], names, Asked(null, above));
    }

    /// <summary>
    /// The reach for one permission, as the rules say it is held: by the owner, for a permission of the resource; by being a
    /// member, for the one permission that gives; through the roles that give it; and from above, where the rules
    /// say a resource is reached so. A permission held in none of these ways reaches nothing.
    /// </summary>
    private EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> ReachFor(string permission, Asking asked)
    {
        var names = Names();
        if (asked.Everything || !asked.SignedIn)
        {
            return new(permission, asked.Now, asked.Everything, MemberHeldBy.None, default, [], names);
        }

        var heldBy = MemberHeldBy.None;
        List<TRoleId> roles = [];
        Func<DbContext, IQueryable<TRoleId>>? kept = null;
        if (asked.Member is not null)
        {
            // The owner holds the permissions the rules give an owner by owning the resource, and nothing through a role.
            if (Rules.OwnerHolds(permission))
            {
                heldBy |= MemberHeldBy.Owner;
            }

            if (Rules.MembershipGives(permission))
            {
                heldBy |= MemberHeldBy.Membership;
            }
            else if (Rules.Roles.Kind == RolesKind.Custom)
            {
                // The roles are rows kept for the resource, and which of them give the permission is read from
                // those rows, inside the statement. Only for a permission a member's role can give: whatever a
                // row still holds from before the rules changed, it gives no permission the rules keep from members.
                if (Rules.RolePermissions.Allows(permission))
                {
                    kept = names.Roles!.ThatGive(permission);
                    heldBy |= MemberHeldBy.Roles;
                }
            }
            else if (_kept is null)
            {
                // The roles are the ones the rules declare, held under their names: checked where the resource was registered.
                roles = [.. Rules.RolesWith(permission).Select(role => (TRoleId)(object)role)];
                if (roles.Count > 0)
                {
                    heldBy |= MemberHeldBy.Roles;
                }
            }
            else if (Rules.RolePermissions.Allows(permission))
            {
                // The roles are kept elsewhere, and which of them give the permission is asked there, inside the
                // statement. Only for a permission a member's role can give: whatever else such a role holds, it
                // gives no permission the rules keep from members.
                var answers = _kept;
                var user = asked.User;
                kept = context => answers.RolesWith(context, user, permission)
                    ?? throw new InvalidOperationException(answers.GetType().Name + " answered no query for the roles that give '" + permission + "'. Answer an empty one for none.");
                heldBy |= MemberHeldBy.Roles;
            }
        }

        // Held where the resource sits, or above it: asked of the application, for members and others alike, for
        // every permission but a permission the rules keep from above.
        var above = Rules.ReachesFromAbove(permission) ? names.Above?.Reaching(_services, asked.User, permission) : null;
        if (above is not null)
        {
            heldBy |= MemberHeldBy.Above;
        }

        return new(permission, asked.Now, false, heldBy, asked.Member ?? default, roles, names, Asked(kept, above));
    }

    /// <summary>What a reach puts into a statement as a query of its own, or <see langword="null"/> when it puts in none.</summary>
    private AskedOfTheApplication<TResource, TRoleId>? Asked(Func<DbContext, IQueryable<TRoleId>>? roles, Func<DbContext, System.Linq.Expressions.Expression<Func<TResource, bool>>>? above)
        => roles is null && above is null ? null : new(roles, above, () => _services.GetService<TContext>());

    private EfMemberPermissionReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> PermissionReachFor(List<string> permissions, Asking asked)
        => new(See(asked), [.. permissions.Select(permission => ReachFor(permission, asked))]);

    /// <summary>
    /// Runs one reading: on a context of its own where the application registered a factory for the context,
    /// disposed when the reading is done, and otherwise on the context of the scope. Where the rows it reads
    /// are under a query filter that reads its context, on the context of the scope wherever it has one: that
    /// rule is the request's context's, as it is for what the member manager asks.
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
    /// <param name="User">The user the question is asked for.</param>
    /// <param name="Everything">Whether the user is the application's own work for this resource.</param>
    /// <param name="SignedIn">Whether the user is a signed-in user: the only user that is a member, or holds a permission from above.</param>
    /// <param name="Member">Who the user is as a member, or nobody.</param>
    /// <param name="Now">The moment the question is asked at.</param>
    private readonly record struct Asking(CurrentUserSnapshot User, bool Everything, bool SignedIn, TMemberId? Member, DateTimeOffset Now);
}
