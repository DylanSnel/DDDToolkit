using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The tenant the current work acts in, and the seat in it. A request acts in the tenant it names, through the user's
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
    /// <summary>A view over the Tenancy user of the current flow of work.</summary>
    internal CurrentTenant()
    {
    }

    /// <summary>Whether the current work acts in a tenant: through a seat, or as system work in the tenant.</summary>
    /// <exception cref="InvalidOperationException">The current user was made with other id types: two Tenancy registrations with different ids.</exception>
    /// <example>
    /// A read that shows nothing outside a tenant, where <see cref="Require"/> would throw:
    /// <code>
    /// if (!access.CurrentTenant.IsAvailable)
    /// {
    ///     return [];
    /// }
    /// </code>
    /// </example>
    public bool IsAvailable => User.Kind is TenancyUserKind.Seat or TenancyUserKind.SystemInTenant;

    /// <summary>The tenant the current work acts in; <see langword="null"/> when it acts in none.</summary>
    /// <exception cref="InvalidOperationException">The current user was made with other id types: two Tenancy registrations with different ids.</exception>
    public TTenantId? Id => User is { Kind: TenancyUserKind.Seat or TenancyUserKind.SystemInTenant } user ? user.Tenant : null;

    /// <summary>
    /// The user's seat in the tenant. For system work in the tenant, the seat it acts for, which is only recorded in the
    /// history and gives no rights; <see langword="null"/> when it acts for none. <see langword="null"/> as well when the
    /// work acts in no tenant.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current user was made with other id types: two Tenancy registrations with different ids.</exception>
    public TSeatId? Seat => User is { Kind: TenancyUserKind.Seat or TenancyUserKind.SystemInTenant } user ? user.Seat : null;

    /// <summary>
    /// The tenant the current work acts in, with the seat, for work that only makes sense inside one. It reads nothing.
    /// A user who is nobody gets the refusal that tenant selection gave the request.
    /// </summary>
    /// <returns>
    /// The tenant and the seat: the user's seat, or for system work in the tenant the seat it acts for, if any, with
    /// <see cref="TenantInScope{TTenantId, TSeatId}.BySystem"/> set.
    /// </returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The user is nobody, with the refusal tenant selection gave: <c>tenancy.tenant-required</c>,
    /// <c>tenancy.not-seated</c>, <c>tenancy.tenant-inactive</c> or <c>tenancy.seat-suspended</c>. Outside any request,
    /// <c>tenancy.not-seated</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The work is system work outside any tenant; or the current user was made with other id types.
    /// </exception>
    /// <example>
    /// <code>
    /// var current = access.CurrentTenant.Require();
    /// var board = Board.Open(BoardId.Create(), current.Id, command.Name, openedBy: current.Seat);
    /// </code>
    /// </example>
    public TenantInScope<TTenantId, TSeatId> Require()
    {
        var user = User;
        return user.Kind switch
        {
            TenancyUserKind.Seat => new TenantInScope<TTenantId, TSeatId>(user.Tenant!.Value, user.Seat, BySystem: false),
            TenancyUserKind.SystemInTenant => new TenantInScope<TTenantId, TSeatId>(user.Tenant!.Value, user.Seat, BySystem: true),
            TenancyUserKind.System => throw new InvalidOperationException(
                "System work outside any tenant has no tenant. "
                + SystemWorkAdvice.BeginInATenant("for work inside one")),
            _ => throw TenancyRefusals.Refuse(user.Refusal ?? TenancyRefusals.NotSeated),
        };
    }

    /// <summary>The Tenancy user now, with the application's ids.</summary>
    private static TenancyUser<TTenantId, TSeatId> User => TenancyUsers.Current<TTenantId, TSeatId>();
}
