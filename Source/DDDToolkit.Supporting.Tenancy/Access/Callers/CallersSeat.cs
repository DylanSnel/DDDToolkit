using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Tenancy's <see cref="ICallersOwn{TValue}"/> for the application's seat id: the caller's own seat is its seat in the
/// tenant of the request, active, in an active tenant. So <c>[OrOwn] SeatId Seat</c> beside a key, and
/// <c>TenancyAccess.Own(seat)</c>, let a seat through for itself. <c>AddTenancy</c> registers it; a host that registers
/// its own with <c>services.AddCallersOwn&lt;SeatId, ...&gt;()</c> after it has that one asked instead.
/// </summary>
/// <remarks>
/// It asks Tenancy's caller, which is found from the caller's verified identity and the tenant the request names, and
/// never from what the request sends. Nobody else has a seat of its own: a caller with no seat in the tenant, an
/// operator, and system work, which holds every key in its tenant and acts for a seat at most as who is recorded, with no
/// rights of that seat's.
/// </remarks>
/// <param name="answers">Tenancy's answers about the current caller.</param>
internal sealed class CallersSeat<TTenantId, TSeatId, TUnitId, TRoleId>(ITenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId> answers) : ICallersOwn<TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <inheritdoc />
    public TSeatId? Find(Caller caller)
        => answers.Caller is { Kind: TenancyCallerKind.Seat, Seat: { } seat } ? seat : null;
}
