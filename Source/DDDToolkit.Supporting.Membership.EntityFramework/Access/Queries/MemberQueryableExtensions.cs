using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Puts what the access questions of a resource decided into a statement of the module's own, so "the
/// documents I may see", "this document, and how I hold the key on it" and "the keys I hold on these
/// documents" are each one statement of the module's context, and never a list of ids fetched first:
/// <code>
/// var seen = access.Reach(DocumentKeys.View);
/// var page = await db.Documents.Within(seen).OrderBy(document =&gt; document.Title).Take(20).ToListAsync(cancellationToken);
/// </code>
/// <para>
/// Each takes a reach the access questions registered for the resource made, for the caller the
/// request runs as, and adds its conditions to the query it is called on: subqueries over the resource's own
/// member rows. The query stays the module's, on whichever context it is running, the request's own or one
/// made for one reading. A reach somebody else made is refused: what a statement is filtered by is always
/// what the rules decided.
/// </para>
/// <para>
/// A reach whose rules have rows say which roles give a key, the resource's own role table or roles the
/// application keeps elsewhere, or have the application say where a key is held above, puts that into the
/// query as well, as a subquery over the context the query runs on. On the request's own context nothing
/// more is said. A query on another context, one made for one reading, hands that context over, as the last
/// argument:
/// <code>
/// await using var db = await contexts.CreateDbContextAsync(cancellationToken);
/// var page = await db.Crates.Within(seen, db).OrderBy(crate =&gt; crate.Label).Take(20).ToListAsync(cancellationToken);
/// </code>
/// </para>
/// </summary>
public static class MemberQueryableExtensions
{
    /// <summary>
    /// Those of <paramref name="resources"/> that <paramref name="reach"/> reaches: every one for the
    /// application's own work, none for a caller that is nobody's member.
    /// </summary>
    /// <param name="resources">A query over the resources, of the module's own context.</param>
    /// <param name="reach">A reach made by the access questions of this kind of resource.</param>
    /// <param name="context">
    /// The context the query runs on, for a query that does not run on the request's own and a reach that puts
    /// a query of its own into it: the rows of the roles kept for the resource, or what the application
    /// answers. Left out otherwise.
    /// </param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> or <paramref name="reach"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reach"/> was not made by the access questions registered for <typeparamref name="TResource"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The reach puts a query of its own into the query, the roles' rows or what the application answers, and
    /// the query runs on another context than <paramref name="context"/>, or than the request's own when none
    /// is handed over.
    /// </exception>
    public static IQueryable<TResource> Within<TResource, TResourceId>(this IQueryable<TResource> resources, MemberReach<TResourceId> reach, DbContext? context = null)
        where TResource : class
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(resources);

        return Own<TResource, TResourceId>(reach).Within(resources, context);
    }

    /// <summary>
    /// Each of <paramref name="resources"/> with how <paramref name="reach"/> reaches it, as a step the query
    /// goes on from: for a statement that answers a resource's own columns next to whether the caller holds a
    /// key on it. Nothing is left out here; put <see cref="Within"/> in front, with the reach that sees, so a
    /// caller is answered only about what it may see:
    /// <code>
    /// var see = access.Reach(DocumentKeys.View);
    /// var act = access.Reach(DocumentKeys.Edit);
    /// var answer = await db.Documents.Where(document =&gt; document.Id == id)
    ///     .Within(see)
    ///     .Reached(act)
    ///     .Select(found =&gt; new { found.Resource.Archived, found.AsMember, found.FromAbove, found.Until })
    ///     .FirstOrDefaultAsync(cancellationToken);
    /// var via = answer is null ? null : act.Via(answer.AsMember, answer.FromAbove);
    /// </code>
    /// <see cref="MemberReach{TResourceId}.Via"/> makes of the two facts how the key is held, the application's
    /// own work included, and <see cref="MemberFound{TResource}.Until"/> is until when it is held as a member,
    /// for a statement that goes on to it.
    /// </summary>
    /// <param name="resources">A query over the resources, of the module's own context.</param>
    /// <param name="reach">A reach made by the access questions of this kind of resource.</param>
    /// <param name="context">
    /// The context the query runs on, for a query that does not run on the request's own and a reach that puts
    /// a query of its own into it: the rows of the roles kept for the resource, or what the application
    /// answers. Left out otherwise.
    /// </param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> or <paramref name="reach"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reach"/> was not made by the access questions registered for <typeparamref name="TResource"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The reach puts a query of its own into the query, the roles' rows or what the application answers, and
    /// the query runs on another context than <paramref name="context"/>, or than the request's own when none
    /// is handed over.
    /// </exception>
    public static IQueryable<MemberFound<TResource>> Reached<TResource, TResourceId>(this IQueryable<TResource> resources, MemberReach<TResourceId> reach, DbContext? context = null)
        where TResource : class
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(resources);

        return Own<TResource, TResourceId>(reach).Reached(resources, context);
    }

    /// <summary>
    /// The keys of <paramref name="reach"/> the caller holds on those of <paramref name="resources"/> it sees,
    /// one row for each key held on a resource, for a statement that reads what its caller may do with each
    /// row of a list. A key is one row on a resource however many ways it is held there.
    /// </summary>
    /// <param name="resources">A query over the resources asked about, of the module's own context.</param>
    /// <param name="reach">A reach made by the access questions of this kind of resource, for the keys asked about.</param>
    /// <param name="context">
    /// The context the query runs on, for a query that does not run on the request's own and a reach that puts
    /// a query of its own into it: the rows of the roles kept for the resource, or what the application
    /// answers. Left out otherwise.
    /// </param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> or <paramref name="reach"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reach"/> was not made by the access questions registered for <typeparamref name="TResource"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The reach puts a query of its own into the query, the roles' rows or what the application answers, and
    /// the query runs on another context than <paramref name="context"/>, or than the request's own when none
    /// is handed over.
    /// </exception>
    public static IQueryable<MemberKeyOn<TResourceId>> KeysOn<TResource, TResourceId>(this IQueryable<TResource> resources, MemberKeyReach<TResourceId> reach, DbContext? context = null)
        where TResource : class
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(reach);

        return reach is IEfMemberKeyReach<TResource, TResourceId> own
            ? own.KeysOn(resources, context)
            : throw NotOurs<TResource>(nameof(reach));
    }

    private static IEfMemberReach<TResource> Own<TResource, TResourceId>(MemberReach<TResourceId> reach)
        where TResource : class
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(reach);

        return reach as IEfMemberReach<TResource> ?? throw NotOurs<TResource>(nameof(reach));
    }

    private static ArgumentException NotOurs<TResource>(string parameter)
        => new(
            "This reach was not made by the access questions registered for " + typeof(TResource).Name + ". "
            + "Ask IMemberQuestions of that resource's id for the reach, and put it into a query over " + typeof(TResource).Name + ".",
            parameter);
}
