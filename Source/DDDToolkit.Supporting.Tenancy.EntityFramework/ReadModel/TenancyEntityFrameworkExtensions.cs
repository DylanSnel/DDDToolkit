using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Asks Tenancy inside a module's own queries.</summary>
public static class TenancyEntityFrameworkExtensions
{
    /// <summary>
    /// The access queries for the current caller over <paramref name="context"/>: Tenancy's own, or a module's
    /// that maps the read model with <c>AddTenancyReadModel</c> or, where the database keeps the rights, with
    /// <c>AddTenancyReadFunctions</c>. What they return are queries of that context, so composed into the
    /// module's own query they become subqueries of one statement:
    /// <code>
    /// var queries = tenancy.In(db);
    /// var mine = db.Projects.Where(project =&gt; queries.UnitsWhereIHold("projects.edit").Contains(project.UnitId));
    /// </code>
    /// <para>
    /// The queries keep nothing of the context but the reference: ask over the context that runs the query they
    /// are composed into, a request's own or one taken from a factory for one query, and they run on that one.
    /// </para>
    /// </summary>
    /// <param name="tenancy">Tenancy, asked about the current caller.</param>
    /// <param name="context">The context to ask over.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tenancy"/> or <paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The database keeps the rights (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>), and
    /// <paramref name="context"/> is a module's that maps the read model as views over Tenancy's tables: there a
    /// module reads no table of Tenancy's, and asks its functions, mapped with <c>AddTenancyReadFunctions</c>.
    /// </exception>
    public static ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId> In<TTenantId, TSeatId, TUnitId, TRoleId>(
        this ITenancy<TTenantId, TSeatId, TUnitId, TRoleId> tenancy,
        DbContext context)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(tenancy);
        ArgumentNullException.ThrowIfNull(context);

        return tenancy.In(new EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>(context), EfQueryExecutor.Instance);
    }
}
