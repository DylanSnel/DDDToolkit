using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The roles a customer makes for one kind of resource, as its statements read them: the rows of the application's role
/// class, in the context that maps the resource. Closed over the role class, which only the model knows.
/// </summary>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
internal abstract class CustomRoles<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>
    /// The roles in use that hold <paramref name="key"/>, for the context a statement runs on: a query over
    /// the role table, which becomes a subquery of that statement. An archived role is not among them.
    /// </summary>
    public abstract Func<DbContext, IQueryable<TRoleId>> ThatGive(string key);

    /// <summary>Whether <paramref name="role"/> is a role in use, among the rows <paramref name="context"/> reads.</summary>
    public abstract Task<bool> InUseAsync(DbContext context, TRoleId role, CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <typeparam name="TRole">The application's role class.</typeparam>
/// <typeparam name="TRoleId">What a role is known by.</typeparam>
internal sealed class CustomRoles<TRole, TRoleId> : CustomRoles<TRoleId>
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <inheritdoc />
    public override Func<DbContext, IQueryable<TRoleId>> ThatGive(string key)
        => context => context.Set<TRole>()
            .Where(role => role.Status == CustomRoleStatus.Active && role.Keys.Contains(key))
            .Select(role => role.Id);

    /// <inheritdoc />
    public override Task<bool> InUseAsync(DbContext context, TRoleId role, CancellationToken cancellationToken)
        => context.Set<TRole>().AnyAsync(kept => kept.Id.Equals(role) && kept.Status == CustomRoleStatus.Active, cancellationToken);
}
