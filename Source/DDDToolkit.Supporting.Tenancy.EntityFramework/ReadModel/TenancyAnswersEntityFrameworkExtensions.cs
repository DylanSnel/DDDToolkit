using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Asks Tenancy inside a module's own queries.</summary>
public static class TenancyAnswersEntityFrameworkExtensions
{
    /// <summary>
    /// The access questions for the current caller over <paramref name="context"/>: Tenancy's own, or a module's
    /// that maps the read model with <c>AddTenancyReadModel</c> or, where the database keeps the rights, with
    /// <c>AddTenancyReadFunctions</c>. What they return are queries of that context, so composed into the
    /// module's own query they become subqueries of one statement:
    /// <code>
    /// var tenancy = answers.Over(db);
    /// var mine = db.Projects.Where(project =&gt; tenancy.UnitsWhereIHold("projects.edit").Contains(project.UnitId));
    /// </code>
    /// <para>
    /// The questions keep nothing of the context but the reference: ask over the context that runs the query they
    /// are composed into, a request's own or one taken from a factory for one query, and they run on that one.
    /// </para>
    /// </summary>
    /// <param name="answers">Tenancy's answers.</param>
    /// <param name="context">The context to ask over.</param>
    /// <exception cref="ArgumentNullException"><paramref name="answers"/> or <paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The database keeps the rights (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>), and
    /// <paramref name="context"/> is a module's that maps the read model as views over Tenancy's tables: there a
    /// module reads no table of Tenancy's, and asks its functions, mapped with <c>AddTenancyReadFunctions</c>.
    /// </exception>
    public static ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId> Over<TTenantId, TSeatId, TUnitId, TRoleId>(
        this ITenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId> answers,
        DbContext context)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(context);

        return answers.Over(new EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>(context), EfQueryExecutor.Instance);
    }
}
