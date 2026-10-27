using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The tenant the current work acts in, and the seat in it. A request acts in the tenant it names, through the caller's
/// seat there. System work acts in the tenant it was begun in. It reads the current flow of work each time it is asked,
/// so it follows every <c>Begin</c>.
/// <code>
/// board.AddMember(command.Seat, command.Role, MemberPeriod.Open(now), now, addedBy: access.CurrentTenant.Seat);
/// </code>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
public sealed class CurrentTenant<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    /// <summary>A view over the Tenancy caller of the current flow of work.</summary>
    internal CurrentTenant()
    {
    }

    /// <summary>Whether the current work acts in a tenant: through a seat, or as system work in the tenant.</summary>
    /// <exception cref="InvalidOperationException">The current caller was made with other id types: two Tenancy registrations with different ids.</exception>
    /// <example>
    /// A read that shows nothing outside a tenant, where <see cref="Require"/> would throw:
    /// <code>
    /// if (!access.CurrentTenant.IsAvailable)
    /// {
    ///     return [];
    /// }
    /// </code>
    /// </example>
    public bool IsAvailable => Caller.Kind is TenancyCallerKind.Seat or TenancyCallerKind.SystemInTenant;

    /// <summary>The tenant the current work acts in; <see langword="null"/> when it acts in none.</summary>
    /// <exception cref="InvalidOperationException">The current caller was made with other id types: two Tenancy registrations with different ids.</exception>
    public TTenantId? Id => Caller is { Kind: TenancyCallerKind.Seat or TenancyCallerKind.SystemInTenant } caller ? caller.Tenant : null;

    /// <summary>
    /// The caller's seat in the tenant. For system work in the tenant, the seat it acts for, which is only recorded in the
    /// history and gives no rights; <see langword="null"/> when it acts for none. <see langword="null"/> as well when the
    /// work acts in no tenant.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current caller was made with other id types: two Tenancy registrations with different ids.</exception>
    public TSeatId? Seat => Caller is { Kind: TenancyCallerKind.Seat or TenancyCallerKind.SystemInTenant } caller ? caller.Seat : null;

    /// <summary>
    /// The tenant the current work acts in, with the seat, for work that only makes sense inside one. It reads nothing.
    /// A caller who is nobody gets the refusal that tenant selection gave the request.
    /// </summary>
    /// <returns>
    /// The tenant and the seat: the caller's seat, or for system work in the tenant the seat it acts for, if any, with
    /// <see cref="TenantInScope{TTenantId, TSeatId}.BySystem"/> set.
    /// </returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller is nobody, with the refusal tenant selection gave: <c>tenancy.tenant-required</c>,
    /// <c>tenancy.not-seated</c>, <c>tenancy.tenant-inactive</c> or <c>tenancy.seat-suspended</c>. Outside any request,
    /// <c>tenancy.not-seated</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The work is system work outside any tenant; or the current caller was made with other id types.
    /// </exception>
    /// <example>
    /// <code>
    /// var current = access.CurrentTenant.Require();
    /// var board = Board.Open(BoardId.Create(), current.Id, command.Name, openedBy: current.Seat);
    /// </code>
    /// </example>
    public TenantInScope<TTenantId, TSeatId> Require()
    {
        var caller = Caller;
        return caller.Kind switch
        {
            TenancyCallerKind.Seat => new TenantInScope<TTenantId, TSeatId>(caller.Tenant!.Value, caller.Seat, BySystem: false),
            TenancyCallerKind.SystemInTenant => new TenantInScope<TTenantId, TSeatId>(caller.Tenant!.Value, caller.Seat, BySystem: true),
            TenancyCallerKind.System => throw new InvalidOperationException(
                "System work outside any tenant has no tenant. "
                + SystemWorkAdvice.BeginInATenant("for work inside one")),
            _ => throw TenancyRefusals.Refuse(caller.Refusal ?? TenancyRefusals.NotSeated),
        };
    }

    /// <summary>The Tenancy caller now, with the application's ids.</summary>
    private static TenancyCaller<TTenantId, TSeatId> Caller => TenancyCallers.Current<TTenantId, TSeatId>();
}
