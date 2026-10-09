using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The roles kept for one kind of resource, as its statements read them: the rows of the application's role
/// class, in the context that maps the resource. Closed over the role class, which only the model knows.
/// </summary>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
internal abstract class KeptRoles<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>
    /// The roles in use that hold <paramref name="key"/>, for the context a statement runs on: a query over
    /// the role table, which becomes a subquery of that statement. An archived role is not among them.
    /// </summary>
    public abstract Func<DbContext, IQueryable<TRoleId>> ThatGive(string key);

    /// <summary>Whether <paramref name="role"/> is a role in use, among the rows <paramref name="context"/> reads.</summary>
    public abstract Task<bool> InUseAsync(DbContext context, TRoleId role, CancellationToken cancellationToken);

    /// <summary>
    /// The role in use that was made from the starter role <paramref name="starter"/>, among the rows
    /// <paramref name="context"/> reads, or <see langword="null"/> when there is none.
    /// </summary>
    /// <exception cref="InvalidOperationException">There are several: the rows of more than one scope were read.</exception>
    public abstract Task<TRoleId?> MadeFromAsync(DbContext context, string starter, CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <typeparam name="TRole">The application's role class.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
internal sealed class KeptRoles<TRole, TRoleId> : KeptRoles<TRoleId>
    where TRole : KeptRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <inheritdoc />
    public override Func<DbContext, IQueryable<TRoleId>> ThatGive(string key)
        => context => context.Set<TRole>()
            .Where(role => role.Status == KeptRoleStatus.Active && role.Keys.Contains(key))
            .Select(role => role.Id);

    /// <inheritdoc />
    public override Task<bool> InUseAsync(DbContext context, TRoleId role, CancellationToken cancellationToken)
        => context.Set<TRole>().AnyAsync(kept => kept.Id.Equals(role) && kept.Status == KeptRoleStatus.Active, cancellationToken);

    /// <inheritdoc />
    public override async Task<TRoleId?> MadeFromAsync(DbContext context, string starter, CancellationToken cancellationToken)
    {
        // Two are enough to tell that there is more than one. Which two does not matter, but a limit without an
        // order is what Entity Framework warns about, once for each query it compiles, so they come in one.
        var made = await context.Set<TRole>()
            .Where(kept => kept.MadeFrom == starter && kept.Status == KeptRoleStatus.Active)
            .OrderBy(kept => kept.Id)
            .Select(kept => kept.Id)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return made.Count switch
        {
            0 => null,
            1 => made[0],
            _ => throw new InvalidOperationException(
                "More than one " + typeof(TRole).Name + " in use was made from the starter role '" + starter + "' among the rows " + context.GetType().Name
                + " reads here, so which of them is meant cannot be told. Roles are kept in scopes, one set for each customer say, and which scope a request is in "
                + "is the application's own rule on its rows: a query filter on " + typeof(TRole).Name + ", or a row access rule. Apply it to the request this was asked in, "
                + "and declare a unique index over the scope's columns and MadeFrom, so a scope has each starter role once."),
        };
    }
}
