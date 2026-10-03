using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// What a reach puts into a statement as a query of its own: the roles that give its key, where the roles are
/// rows, read from the resource's own role table or answered by the application for roles kept elsewhere;
/// and where the caller holds the key above, which the application answers. Each is a query over the context
/// the statement runs on, so each is made with that context, when the statement is put together.
/// </summary>
/// <param name="Roles">The roles that give the key, or <see langword="null"/> when the rules declare the roles.</param>
/// <param name="Above">The condition that a resource is reached from above, or <see langword="null"/> when it is not.</param>
/// <param name="RequestContext">The request's own context, or <see langword="null"/> when the scope has none: the one a statement runs on unless it is told another.</param>
internal sealed record AskedOfTheApplication<TResource, TRoleId>(
    Func<DbContext, IQueryable<TRoleId>>? Roles,
    Func<DbContext, Expression<Func<TResource, bool>>>? Above,
    Func<DbContext?> RequestContext)
    where TResource : class;
