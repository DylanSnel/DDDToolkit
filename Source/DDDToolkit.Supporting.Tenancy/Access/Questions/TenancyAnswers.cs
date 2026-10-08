using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Answers what the current caller may do, over any read source. Registered once for the application; it
/// keeps no state of its own, and reads the caller and the time each time it is asked.
/// </summary>
/// <param name="catalogue">The catalogue every key asked about is checked against.</param>
/// <param name="clock">What "now" is, for whether a grant applies.</param>
public sealed class TenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId>(TenancyCatalogue catalogue, TimeProvider clock)
    : ITenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <inheritdoc />
    public TenancyCaller<TTenantId, TSeatId> Caller => TenancyCallers.Current<TTenantId, TSeatId>();

    /// <inheritdoc />
    public TenantInScope<TTenantId, TSeatId> RequireTenant()
    {
        var caller = Caller;
        return caller.Kind switch
        {
            TenancyCallerKind.Seat => new TenantInScope<TTenantId, TSeatId>(caller.Tenant!.Value, caller.Seat, BySystem: false),
            TenancyCallerKind.SystemInTenant => new TenantInScope<TTenantId, TSeatId>(caller.Tenant!.Value, caller.Seat, BySystem: true),
            TenancyCallerKind.System => throw new InvalidOperationException(
                "System work outside any tenant has no tenant. Begin TenancyWork.BeginSystemIn(tenant) for work inside one."),
            _ => throw TenancyRefusals.Refuse(caller.Refusal ?? TenancyRefusals.NotSeated),
        };
    }

    /// <inheritdoc />
    public ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId> Over(
        ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> source,
        IQueryExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(executor);

        return new TenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId>(source, executor, catalogue, Caller, clock.GetUtcNow());
    }
}
