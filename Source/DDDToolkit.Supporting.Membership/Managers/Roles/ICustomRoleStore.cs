using System.Linq.Expressions;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// Where the role manager of one role class loads, adds and saves roles: the unit of work of the scope, under the
/// rules the application's context keeps its rows to, such as the customer a role is of. A storage package implements it
/// and registers it with the resource the roles are of; an application never takes it.
/// </summary>
/// <typeparam name="TRole">The application's role class.</typeparam>
/// <typeparam name="TRoleId">The role class's id.</typeparam>
public interface ICustomRoleStore<TRole, TRoleId>
    where TRole : CustomRoleAggregate<TRoleId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The role, tracked for a change and its save; or <see langword="null"/> when it is not there for the caller.</summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<TRole?> FindAsync(TRoleId role, CancellationToken cancellationToken);

    /// <summary>
    /// The roles there are for the caller, archived ones included, within <paramref name="scope"/> where one is given:
    /// tracked, for the starter roles made beside them, or read for showing.
    /// </summary>
    /// <param name="scope">The roles of one scope of the application's own, or <see langword="null"/> for every role there is for the caller.</param>
    /// <param name="tracked">Whether the unit of work tracks them.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<TRole>> ListAsync(Expression<Func<TRole, bool>>? scope, bool tracked, CancellationToken cancellationToken);

    /// <summary>The roles of <paramref name="roles"/> there are for the caller, read for showing.</summary>
    /// <param name="roles">The roles asked about.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<TRole>> ByIdAsync(IReadOnlyCollection<TRoleId> roles, CancellationToken cancellationToken);

    /// <summary>Adds a new role to the unit of work.</summary>
    /// <param name="role">The role, just made.</param>
    void Add(TRole role);

    /// <summary>Saves everything the unit of work holds, with the domain events raised, in one transaction.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    Task SaveAsync(CancellationToken cancellationToken);
}
