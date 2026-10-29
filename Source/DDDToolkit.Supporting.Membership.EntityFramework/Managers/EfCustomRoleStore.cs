using System.Linq.Expressions;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Where the role manager of one role class loads, adds and saves roles: the request's own context, its unit of work,
/// under the rules the context keeps the rows to, the filter of a role class kept per organization among them.
/// </summary>
/// <param name="services">The services of the scope.</param>
internal sealed class EfCustomRoleStore<TContext, TRole, TRoleId>(IServiceProvider services) : ICustomRoleStore<TRole, TRoleId>
    where TContext : DbContext
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The request's own context.</summary>
    private TContext Context => services.GetRequiredService<TContext>();

    /// <inheritdoc />
    public Task<TRole?> FindAsync(TRoleId role, CancellationToken cancellationToken)
        => Context.Set<TRole>().AsTracking().FirstOrDefaultAsync(candidate => candidate.Id.Equals(role), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TRole>> ListAsync(Expression<Func<TRole, bool>>? scope, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<TRole> roles = tracked ? Context.Set<TRole>().AsTracking() : Context.Set<TRole>().AsNoTracking();
        if (scope is not null)
        {
            roles = roles.Where(scope);
        }

        return await roles.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TRole>> ByIdAsync(IReadOnlyCollection<TRoleId> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var about = roles.Distinct().ToList();
        return await Context.Set<TRole>().AsNoTracking().Where(candidate => about.Contains(candidate.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(TRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        Context.Set<TRole>().Add(role);
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken) => Context.SaveChangesAsync(cancellationToken);
}
