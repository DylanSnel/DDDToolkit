using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Asks Membership inside a statement of your own, on the resource's own set: "the boards I may see" and "the
/// permissions I hold on these boards" are each one statement of the context, and never a list of ids fetched first.
/// <code>
/// var boards = await db.Boards.WhereIHold(BoardPermissions.View).OrderBy(board =&gt; board.Name).ToListAsync(cancellationToken);
/// </code>
/// <para>
/// The caller is the one current when the query is made, and the moment is fixed then: every part of one query
/// answers for the same caller at the same moment. The rules, and what they ask of the application, come from the
/// services the context was made with: the scope it is bound to (<see cref="PooledContexts.BindToScope{TContext}"/>,
/// which <see cref="PooledContexts.AddScopedFromPool{TContext}"/> does for a scope's own context), or else the services
/// its options were built with, which for <c>AddDbContext</c> are the scope's.
/// </para>
/// <para>
/// What a query is filtered by puts subqueries over the resource's own member rows into it, and, where the rules have
/// rows say which roles give a permission or have the application say where a permission is held above, queries of the
/// context the statement runs on. A query on the set finds its context itself. A query that does not start at a set,
/// one composed of others, hands its context over, as the last argument:
/// <code>
/// var open = db.Boards.Where(board =&gt; !board.Archived).WhereIHold(BoardPermissions.View, db);
/// </code>
/// </para>
/// <para>
/// <see cref="Within"/> and <see cref="Reached"/> are the lower level, for a statement that reads how each resource is
/// held: they take a reach the resource's access manager made (<see cref="MembershipAccessManager{TResource}.Reach"/>).
/// </para>
/// </summary>
public static class MemberQueryableExtensions
{
    /// <summary>
    /// The resources of the set the caller holds <paramref name="permission"/> on: those it is a member of now with a
    /// role that gives the permission, for a permission that being a member gives those it is a member of at all, for a
    /// permission of the resource those it owns, and, where the rules let a resource be reached from above, those that sit
    /// where the caller holds the permission, or below. Every resource for the application's own work; none for a caller
    /// that is nobody.
    /// </summary>
    /// <param name="resources">The resource's set on a context.</param>
    /// <param name="permission">A permission.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// The resource is not registered with Membership in the services the context was made with, or with another
    /// context; or what its rules ask of the application is registered per scope, and the context is in none.
    /// </exception>
    public static IQueryable<TResource> WhereIHold<TResource>(this DbSet<TResource> resources, string permission)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(resources);

        var context = resources.GetService<ICurrentDbContext>().Context;
        return QueriesOn<TResource>(context).WhereIHold(resources, permission, context);
    }

    /// <summary>
    /// Those of <paramref name="resources"/> the caller holds <paramref name="permission"/> on, as
    /// <see cref="WhereIHold{TResource}(DbSet{TResource}, string)"/> answers it, for a query that does not start at the set.
    /// </summary>
    /// <param name="resources">A query over the resources, of <paramref name="context"/>.</param>
    /// <param name="permission">A permission.</param>
    /// <param name="context">The context the query runs on.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> or <paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// The resource is not registered with Membership in the services <paramref name="context"/> was made with; what its
    /// rules ask of the application is registered per scope, and the context is in none; or the query runs on another
    /// context than <paramref name="context"/>.
    /// </exception>
    public static IQueryable<TResource> WhereIHold<TResource>(this IQueryable<TResource> resources, string permission, DbContext context)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(context);

        return QueriesOn<TResource>(context).WhereIHold(resources, permission, context);
    }

    /// <summary>
    /// Which of <paramref name="permissions"/> the caller holds on which resources of the set: one pair of resource and
    /// permission for each permission held on a resource it sees, for a list that shows what its caller may do with each
    /// row, in one statement. A resource the caller does not see is in no pair; the application's own work holds every
    /// permission on every resource.
    /// <code>
    /// var held = await db.Boards.WhereIHold([BoardPermissions.Edit, BoardPermissions.Archive])
    ///     .Select(pair =&gt; new { pair.Resource.Id, pair.Permission })
    ///     .ToListAsync(cancellationToken);
    /// </code>
    /// </summary>
    /// <param name="resources">The resource's set on a context.</param>
    /// <param name="permissions">Permissions, none of them blank.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> or <paramref name="permissions"/> is null.</exception>
    /// <exception cref="ArgumentException">A permission is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// The resource is not registered with Membership in the services the context was made with, or with another
    /// context; or what its rules ask of the application is registered per scope, and the context is in none.
    /// </exception>
    public static IQueryable<PermissionOn<TResource>> WhereIHold<TResource>(this DbSet<TResource> resources, IReadOnlyCollection<string> permissions)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(resources);

        var context = resources.GetService<ICurrentDbContext>().Context;
        return QueriesOn<TResource>(context).WhereIHold(resources, permissions, context);
    }

    /// <summary>
    /// Which of <paramref name="permissions"/> the caller holds on which of <paramref name="resources"/>, as
    /// <see cref="WhereIHold{TResource}(DbSet{TResource}, IReadOnlyCollection{string})"/> answers it, for a query that does
    /// not start at the set.
    /// </summary>
    /// <param name="resources">A query over the resources asked about, of <paramref name="context"/>.</param>
    /// <param name="permissions">Permissions, none of them blank.</param>
    /// <param name="context">The context the query runs on.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A permission is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// The resource is not registered with Membership in the services <paramref name="context"/> was made with; what its
    /// rules ask of the application is registered per scope, and the context is in none; or the query runs on another
    /// context than <paramref name="context"/>.
    /// </exception>
    public static IQueryable<PermissionOn<TResource>> WhereIHold<TResource>(this IQueryable<TResource> resources, IReadOnlyCollection<string> permissions, DbContext context)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(context);

        return QueriesOn<TResource>(context).WhereIHold(resources, permissions, context);
    }

    /// <summary>
    /// Those of <paramref name="resources"/> that <paramref name="reach"/> reaches: every one for the application's own
    /// work, none for a caller that is nobody's member. The lower level under
    /// <see cref="WhereIHold{TResource}(IQueryable{TResource}, string, DbContext)"/>, for a reach made once and put into
    /// several statements, or made in one place and put into a statement in another.
    /// <code>
    /// var seen = boardAccess.Reach(BoardPermissions.View);
    /// var page = await db.Boards.Within(seen).OrderBy(board =&gt; board.Name).Take(20).ToListAsync(cancellationToken);
    /// </code>
    /// </summary>
    /// <param name="resources">A query over the resources.</param>
    /// <param name="reach">A reach made by the access manager of this kind of resource.</param>
    /// <param name="context">
    /// The context the query runs on, for a query that does not run on the request's own and a reach that puts a query
    /// of its own into it: the rows of the roles kept for the resource, or what the application answers. Left out otherwise.
    /// </param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> or <paramref name="reach"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reach"/> was not made by the access manager registered for <typeparamref name="TResource"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The reach puts a query of its own into the query, the roles' rows or what the application answers, and the query
    /// runs on another context than <paramref name="context"/>, or than the request's own when none is handed over.
    /// </exception>
    public static IQueryable<TResource> Within<TResource>(this IQueryable<TResource> resources, MemberReach<TResource> reach, DbContext? context = null)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(resources);

        return Own(reach).Within(resources, context);
    }

    /// <summary>
    /// Each of <paramref name="resources"/> with how <paramref name="reach"/> reaches it, as a step the query goes on
    /// from: for a statement that answers a resource's own columns next to whether the caller holds a permission on it.
    /// Nothing is left out here; put <see cref="WhereIHold{TResource}(IQueryable{TResource}, string, DbContext)"/> or
    /// <see cref="Within"/> in front, with the permission that sees, so a caller is answered only about what it may see:
    /// <code>
    /// var act = boardAccess.Reach(BoardPermissions.Edit);
    /// var answer = await db.Boards.Where(board =&gt; board.Id == id)
    ///     .WhereIHold(BoardPermissions.View, db)
    ///     .Reached(act)
    ///     .Select(found =&gt; new { found.Resource.Archived, found.AsMember, found.FromAbove, found.Until })
    ///     .FirstOrDefaultAsync(cancellationToken);
    /// var via = answer is null ? null : act.Via(answer.AsMember, answer.FromAbove);
    /// </code>
    /// <see cref="MemberReach{TResource}.Via"/> makes of the two facts how the permission is held, the application's own
    /// work included, and <see cref="MemberFound{TResource}.Until"/> is until when it is held as a member, for a statement
    /// that goes on to it.
    /// </summary>
    /// <param name="resources">A query over the resources.</param>
    /// <param name="reach">A reach made by the access manager of this kind of resource.</param>
    /// <param name="context">
    /// The context the query runs on, for a query that does not run on the request's own and a reach that puts a query
    /// of its own into it. Left out otherwise.
    /// </param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> or <paramref name="reach"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reach"/> was not made by the access manager registered for <typeparamref name="TResource"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The reach puts a query of its own into the query, and the query runs on another context than
    /// <paramref name="context"/>, or than the request's own when none is handed over.
    /// </exception>
    public static IQueryable<MemberFound<TResource>> Reached<TResource>(this IQueryable<TResource> resources, MemberReach<TResource> reach, DbContext? context = null)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(resources);

        return Own(reach).Reached(resources, context);
    }

    /// <summary>
    /// The queries of <typeparamref name="TResource"/> for the current caller, made with the services of the scope
    /// <paramref name="context"/> is in: the one it is bound to, or else the ones its options were built with.
    /// </summary>
    /// <exception cref="InvalidOperationException">They cannot be made there.</exception>
    private static IMemberQueries<TResource> QueriesOn<TResource>(DbContext context)
        where TResource : class
    {
        var services = context.ScopeOf()
            ?? context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?? throw new InvalidOperationException(
                "'" + context.GetType().Name + "' was made without the application's services, and which of " + typeof(TResource).Name
                + " the caller holds a permission on is asked of the rules registered there. Register the context with AddDbContext, or take it from a pool "
                + "and bind it to the scope: context.BindToScope(scope.ServiceProvider).");

        var queries = services.GetService<MemberQueriesOf<TResource>>()
            ?? throw new InvalidOperationException(
                typeof(TResource).Name + " is not registered with Membership in the services '" + context.GetType().Name + "' was made with. "
                + "Register it with the context that maps it: services.Add" + typeof(TResource).Name + "Membership<" + context.GetType().Name + ">(rules).");

        if (!queries.Context.IsInstanceOfType(context))
        {
            throw new InvalidOperationException(
                typeof(TResource).Name + " is registered with Membership in " + queries.Context.Name + ", and this query runs on '" + context.GetType().Name + "'. "
                + "Ask on a context of the one it is registered with.");
        }

        try
        {
            return queries.For(services);
        }
        catch (InvalidOperationException failed)
        {
            // What the rules ask of the application, who the caller is as a member or where it holds a permission above,
            // is the application's to answer per scope, and a context of a pool or a factory is in none unless it is bound.
            throw new InvalidOperationException(
                "The rules of " + typeof(TResource).Name + " ask the application's services of a scope, and '" + context.GetType().Name + "' could not give them: "
                + failed.Message + " Ask on the scope's own context, or bind a context of a pool or a factory to the scope first: context.BindToScope(scope.ServiceProvider).",
                failed);
        }
    }

    private static IEfMemberReach<TResource> Own<TResource>(MemberReach<TResource> reach)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(reach);

        return reach as IEfMemberReach<TResource>
               ?? throw new ArgumentException(
                   "This reach was not made by the access manager registered for " + typeof(TResource).Name + ". "
                   + "Ask MembershipAccessManager<" + typeof(TResource).Name + "> for the reach, and put it into a query over " + typeof(TResource).Name + ".",
                   nameof(reach));
    }
}
