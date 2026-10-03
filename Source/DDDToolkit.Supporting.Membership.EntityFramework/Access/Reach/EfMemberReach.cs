using System.Linq.Expressions;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The reach the access questions of a resource stored with Entity Framework make: what the rules decided for
/// one key and one caller, as conditions over the resource's own rows, and over what the application answers
/// where the rules say it does. It reads nothing itself; it becomes part of whichever statement it is put
/// into.
/// </summary>
internal sealed class EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> : MemberReach<TResourceId>, IEfMemberReach<TResource>
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly MemberNames<TResource, TRoleId> _names;
    private readonly TMemberId _member;
    private readonly List<TRoleId> _roles;
    private readonly AskedOfTheApplication<TResource, TRoleId>? _asked;

    /// <summary>A reach for <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="now">The moment a membership, and a role held in it, must apply at.</param>
    /// <param name="everything">Whether the caller is the application's own work, which reaches every resource.</param>
    /// <param name="heldBy">How the caller can hold the key; <see cref="MemberHeldBy.None"/> for a caller that holds it in no way.</param>
    /// <param name="member">Who the caller is as a member, for the ways that ask it.</param>
    /// <param name="roles">The roles that give the key, for <see cref="MemberHeldBy.Roles"/> under rules that declare the roles.</param>
    /// <param name="names">The properties of the resource the conditions read.</param>
    /// <param name="asked">What the reach puts into a statement as a query of its own, or <see langword="null"/> when it puts in none.</param>
    public EfMemberReach(
        string key,
        DateTimeOffset now,
        bool everything,
        MemberHeldBy heldBy,
        TMemberId member,
        List<TRoleId> roles,
        MemberNames<TResource, TRoleId> names,
        AskedOfTheApplication<TResource, TRoleId>? asked = null)
        : base(key, now, everything)
    {
        HeldBy = everything ? MemberHeldBy.None : heldBy;
        _member = member;
        _roles = roles;
        _names = names;
        _asked = everything ? null : asked;
    }

    /// <summary>The reach of what the caller sees, under rules that name no key for it.</summary>
    /// <param name="now">The moment a membership must apply at.</param>
    /// <param name="everything">Whether the caller is the application's own work, which reaches every resource.</param>
    /// <param name="heldBy">How a member sees a resource; <see cref="MemberHeldBy.None"/> for a caller that is nobody's member.</param>
    /// <param name="member">Who the caller is as a member.</param>
    /// <param name="names">The properties of the resource the conditions read.</param>
    public EfMemberReach(DateTimeOffset now, bool everything, MemberHeldBy heldBy, TMemberId member, MemberNames<TResource, TRoleId> names)
        : base(now, everything)
    {
        HeldBy = everything ? MemberHeldBy.None : heldBy;
        _member = member;
        _roles = [];
        _names = names;
    }

    /// <summary>How the caller can hold the key; nothing for the application's own work, which holds it anyway.</summary>
    public MemberHeldBy HeldBy { get; }

    /// <summary>Whether the caller reaches nothing: it holds the key in no way the rules know.</summary>
    public bool Nothing => !Everything && HeldBy == MemberHeldBy.None;

    /// <inheritdoc />
    public IQueryable<TResource> Within(IQueryable<TResource> resources, DbContext? context)
        => Everything ? resources : Where(resources, Held(ContextOf(resources, context)));

    /// <inheritdoc />
    public IQueryable<MemberFound<TResource>> Reached(IQueryable<TResource> resources, DbContext? context)
        => Found(resources, context, whileSeen: null);

    /// <summary>
    /// Each of <paramref name="resources"/> the caller sees, as <paramref name="see"/> says it, with how this
    /// reach reaches it: what a hold is read from, in one statement.
    /// <para>
    /// Its <see cref="MemberFound{TResource}.Until"/> is until when the caller holds the key on the resource,
    /// however it holds it. A key held from above has no end known here, and is held on a resource for as
    /// long as the caller sees that resource: with no end where it sees it from above as well, and until its
    /// membership ends where it sees it as a member alone.
    /// </para>
    /// </summary>
    /// <param name="resources">The resources asked about.</param>
    /// <param name="context">The context the statement runs on, or <see langword="null"/> for the request's own.</param>
    /// <param name="see">The reach of what the caller sees, made for the same caller at the same moment.</param>
    public IQueryable<MemberFound<TResource>> HeldWhereSeen(
        IQueryable<TResource> resources,
        DbContext? context,
        EfMemberReach<TResource, TResourceId, TMember, TId, TMemberId, TRoleId> see)
    {
        if (Everything || !HeldBy.HasFlag(MemberHeldBy.Above))
        {
            // Nothing is held from above here, so a hold ends when the members say: a member holds a key
            // no longer than it is one.
            return Reached(see.Within(resources, context), context);
        }

        // What lets the caller see a resource is asked for once: it is the filter, and how long a hold from above lasts.
        var statement = see.ContextOf(resources, context);
        var seen = see.Held(statement);

        return Found(Where(resources, seen), context, resource => see.SeenUntil(resource, seen.FromAbove, statement));
    }

    private static Expression NoEnd => Expression.Constant(null, typeof(DateTimeOffset?));

    /// <summary>Those of <paramref name="resources"/> a key is held on in either way, and none of them where it can be held in neither.</summary>
    private static IQueryable<TResource> Where(
        IQueryable<TResource> resources,
        (Expression<Func<TResource, bool>>? AsMember, Expression<Func<TResource, bool>>? FromAbove) held)
    {
        var (asMember, fromAbove) = held;
        var either = asMember is null ? fromAbove
            : fromAbove is null ? asMember
            : Expression.Lambda<Func<TResource, bool>>(Expression.OrElse(asMember.Body, About(fromAbove, asMember.Parameters[0])), asMember.Parameters);

        return either is null ? resources.Where(resource => false) : resources.Where(either);
    }

    /// <summary>
    /// Each of <paramref name="resources"/> with how this reach reaches it. One projection whatever the caller,
    /// so a statement has the same shape for everybody: the resource, whether the caller holds the key on it
    /// as a member, and until when, and whether it holds the key from above, each a subquery of the statement.
    /// </summary>
    /// <param name="resources">The resources.</param>
    /// <param name="context">The context the statement runs on, or <see langword="null"/> for the request's own.</param>
    /// <param name="whileSeen">
    /// Until when the caller sees a resource, where a key held from above is to be held that long;
    /// <see langword="null"/> to say until when the key is held as a member and nothing of a hold from above.
    /// </param>
    private IQueryable<MemberFound<TResource>> Found(IQueryable<TResource> resources, DbContext? context, Func<ParameterExpression, Expression?>? whileSeen)
    {
        var statement = Everything ? null : ContextOf(resources, context);
        var (asMember, fromAbove) = Everything ? (null, null) : Held(statement);
        var resource = asMember?.Parameters[0] ?? Expression.Parameter(typeof(TResource), "resource");
        var above = fromAbove is null ? null : About(fromAbove, resource);
        var until = Everything ? null : HeldUntil(resource, statement);
        if (above is not null && whileSeen is not null)
        {
            // Held from above, the key outlasts whatever the members say, for as long as the resource is seen.
            var seen = whileSeen(resource);
            until = seen is not null ? Expression.Condition(above, seen, until ?? NoEnd)
                : until is not null ? Expression.Condition(above, NoEnd, until)
                : null;
        }

        var found = Expression.MemberInit(
            Expression.New(typeof(MemberFound<TResource>)),
            Expression.Bind(typeof(MemberFound<TResource>).GetProperty(nameof(MemberFound<TResource>.Resource))!, resource),
            Expression.Bind(typeof(MemberFound<TResource>).GetProperty(nameof(MemberFound<TResource>.AsMember))!, asMember?.Body ?? Expression.Constant(false)),
            Expression.Bind(typeof(MemberFound<TResource>).GetProperty(nameof(MemberFound<TResource>.FromAbove))!, above ?? Expression.Constant(false)),
            Expression.Bind(typeof(MemberFound<TResource>).GetProperty(nameof(MemberFound<TResource>.Until))!, until ?? NoEnd));

        return resources.Select(Expression.Lambda<Func<TResource, MemberFound<TResource>>>(found, resource));
    }

    /// <summary>
    /// Until when the caller sees a resource through this reach, the reach of what it sees, as an expression
    /// about the resource <paramref name="about"/> stands for; <see langword="null"/> where that has no end
    /// known here at all, for a caller that is nobody's member and sees a resource from above alone. A
    /// resource seen from above is seen with no end known here; one seen as a member alone, until that
    /// membership ends.
    /// </summary>
    /// <param name="about">The resource.</param>
    /// <param name="fromAbove">The condition that the caller sees a resource from above, or <see langword="null"/> where it cannot.</param>
    /// <param name="context">The context the statement runs on.</param>
    private Expression? SeenUntil(ParameterExpression about, Expression<Func<TResource, bool>>? fromAbove, DbContext? context)
    {
        var asMember = HeldUntil(about, context);

        return asMember is null ? null
            : fromAbove is null ? asMember
            : Expression.Condition(About(fromAbove, about), NoEnd, asMember);
    }

    /// <summary>
    /// The context the statement <paramref name="resources"/> is part of runs on, for a reach that puts a
    /// query of its own into it: <paramref name="given"/>, or the request's own. <see langword="null"/> for
    /// a reach that puts in none, which needs no context.
    /// </summary>
    /// <exception cref="InvalidOperationException">The statement runs on another context than the one known here.</exception>
    private DbContext? ContextOf(IQueryable<TResource> resources, DbContext? given)
    {
        if (_asked is null)
        {
            return null;
        }

        var context = given ?? _asked.RequestContext();
        if (context is not null && ReferenceEquals(context.GetService<IAsyncQueryProvider>(), resources.Provider))
        {
            return context;
        }

        // What goes into the statement is a query over one context's rows, and Entity Framework puts two
        // queries together only when both are of the very same context.
        throw new InvalidOperationException(
            given is not null
                ? "The context handed over with this reach is not the one the query over " + typeof(TResource).Name + " runs on. Hand over the context the query was made from."
                : (_names.Roles is null
                      ? "This reach of " + typeof(TResource).Name + " puts what the application answers into the query, the roles that give the key or where the key is held above, "
                        + "and that is asked for the context the query runs on. "
                      : "This reach of " + typeof(TResource).Name + " puts the roles that give the key into the query, the rows of its role class, "
                        + "and those are read on the context the query runs on. ")
                  + "This query does not run on the request's own context: hand over the context it was made from, "
                  + "as in db.Set<" + typeof(TResource).Name + ">().Within(reach, db).");
    }

    /// <summary>
    /// Whether the caller holds the key on a resource, as two conditions about that resource. As one of its
    /// members: by owning it, by being a member now, or by a role held now, whichever of these the rules let
    /// the key be held by. And from above: by holding the key where the resource sits. Each is
    /// <see langword="null"/> for a reach that does not ask it.
    /// </summary>
    /// <remarks>
    /// What the conditions compare with is copied into locals first, so Entity Framework sees values to send
    /// as parameters, and the sets the application answers as subqueries, where it would otherwise see members
    /// of this object.
    /// </remarks>
    private (Expression<Func<TResource, bool>>? AsMember, Expression<Func<TResource, bool>>? FromAbove) Held(DbContext? context)
    {
        var members = _names.Members;
        var owner = _names.Owner;
        var member = _member;
        var now = Now;
        var roles = Roles(context);

        // A key of the resource is held by matching the owner, never through a role.
        Expression<Func<TResource, bool>>? owns = HeldBy.HasFlag(MemberHeldBy.Owner)
            ? resource => EF.Property<TMemberId>(resource, owner).Equals(member)
            : null;

        Expression<Func<TResource, bool>>? joined =

            // A membership that applies now, whatever roles it holds, or none.
            HeldBy.HasFlag(MemberHeldBy.Membership) ? resource => EF.Property<IEnumerable<TMember>>(resource, members).Any(row =>
                row.MemberId.Equals(member)
                && row.StartsAt <= now
                && (row.EndsAt == null || row.EndsAt > now))

            // A role that gives the key, held now, in a membership that applies now: a role counts only inside
            // its membership.
            : HeldBy.HasFlag(MemberHeldBy.Roles) ? resource => EF.Property<IEnumerable<TMember>>(resource, members).Any(row =>
                row.MemberId.Equals(member)
                && row.StartsAt <= now
                && (row.EndsAt == null || row.EndsAt > now)
                && row.Roles.Any(held =>
                    roles.Contains(held.RoleId)
                    && held.StartsAt <= now
                    && (held.EndsAt == null || held.EndsAt > now)))
            : null;

        var asMember = owns is null ? joined
            : joined is null ? owns
            : Expression.Lambda<Func<TResource, bool>>(Expression.OrElse(owns.Body, About(joined, owns.Parameters[0])), owns.Parameters);

        // Held where the resource sits, or above it: what the application answers, whoever the members are.
        var fromAbove = HeldBy.HasFlag(MemberHeldBy.Above) ? _asked!.Above!(context!) : null;

        return (asMember, fromAbove);
    }

    /// <summary>
    /// The roles that give the key: the ones the rules declare, as values, or, where the roles are rows, the
    /// ones their table or the application answers, as a query of the statement's own context.
    /// </summary>
    private IEnumerable<TRoleId> Roles(DbContext? context)
        => HeldBy.HasFlag(MemberHeldBy.Roles) && _asked?.Roles is { } kept ? kept(context!) : _roles;

    /// <summary>
    /// The first moment the caller no longer holds the key on a resource as one of its members, as its
    /// membership and its roles stand now, as an expression about the resource <paramref name="about"/> stands
    /// for; <see langword="null"/> where that is never, for a reach that asks nothing of the members.
    /// <para>
    /// An owner holds a key of the resource with no end. A key that being a member gives is held until the
    /// membership ends. A key a role gives is held until the last of the roles that give it now ends, and no
    /// longer than the membership those roles are held in: a role counts only inside its membership.
    /// </para>
    /// </summary>
    private Expression? HeldUntil(ParameterExpression about, DbContext? context)
    {
        var members = _names.Members;
        var owner = _names.Owner;
        var member = _member;
        var now = Now;
        var roles = Roles(context);

        Expression<Func<TResource, DateTimeOffset?>>? joined =
            HeldBy.HasFlag(MemberHeldBy.Membership) ? resource => EF.Property<IEnumerable<TMember>>(resource, members)
                .Where(row =>
                    row.MemberId.Equals(member)
                    && row.StartsAt <= now
                    && (row.EndsAt == null || row.EndsAt > now))
                .Select(row => row.EndsAt)
                .FirstOrDefault()

            : HeldBy.HasFlag(MemberHeldBy.Roles) ? resource => EF.Property<IEnumerable<TMember>>(resource, members)
                .Where(row =>
                    row.MemberId.Equals(member)
                    && row.StartsAt <= now
                    && (row.EndsAt == null || row.EndsAt > now)
                    && row.Roles.Any(held =>
                        roles.Contains(held.RoleId)
                        && held.StartsAt <= now
                        && (held.EndsAt == null || held.EndsAt > now)))
                .Select(row =>

                    // A role with no end of its own that gives the key now: the membership's end is the hold's.
                    row.Roles.Any(held => roles.Contains(held.RoleId) && held.StartsAt <= now && held.EndsAt == null)
                        ? row.EndsAt

                        // Otherwise the roles that give the key now all end: the last of them, or the membership if it ends sooner.
                        : row.EndsAt == null
                          || row.Roles.Where(held => roles.Contains(held.RoleId) && held.StartsAt <= now && held.EndsAt > now).Max(held => held.EndsAt) < row.EndsAt
                            ? row.Roles.Where(held => roles.Contains(held.RoleId) && held.StartsAt <= now && held.EndsAt > now).Max(held => held.EndsAt)
                            : row.EndsAt)
                .FirstOrDefault()
            : null;

        if (joined is null)
        {
            // Nothing but owning the resource holds the key as a member, and that has no end.
            return null;
        }

        var asMember = About(joined, about);
        if (!HeldBy.HasFlag(MemberHeldBy.Owner))
        {
            return asMember;
        }

        Expression<Func<TResource, bool>> owns = resource => EF.Property<TMemberId>(resource, owner).Equals(member);
        return Expression.Condition(About(owns, about), NoEnd, asMember);
    }

    /// <summary>The body of <paramref name="condition"/>, said about <paramref name="resource"/> instead of its own parameter.</summary>
    private static Expression About<T>(Expression<Func<TResource, T>> condition, ParameterExpression resource)
        => new Rebound(condition.Parameters[0], resource).Visit(condition.Body);

    /// <summary>Puts one parameter in the place of another, so two conditions are about the same resource.</summary>
    private sealed class Rebound(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
