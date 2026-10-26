using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The door a module goes through to ask Tenancy about the current caller: who it is, which tenant it acts
/// in, and, over a read source the module supplies, what it may do.
/// <code>
/// var tenancy = answers.Over(source, executor);
/// var mine = projects.Where(project => tenancy.UnitsWhereIHold("projects.edit").Contains(project.UnitId));
/// </code>
/// </summary>
public interface ITenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The current Tenancy caller.</summary>
    TenancyCaller<TTenantId, TSeatId> Caller { get; }

    /// <summary>The tenant the current caller acts in, for work that only makes sense inside one.</summary>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody; the refusal is the one it was given.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    TenantInScope<TTenantId, TSeatId> RequireTenant();

    /// <summary>The access questions for the current caller, over <paramref name="source"/>, at this moment.</summary>
    /// <param name="source">The rows to ask over, usually in the module's own context.</param>
    /// <param name="executor">Runs the questions that answer yes or no.</param>
    ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId> Over(
        ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
        IQueryExecutor executor);
}
