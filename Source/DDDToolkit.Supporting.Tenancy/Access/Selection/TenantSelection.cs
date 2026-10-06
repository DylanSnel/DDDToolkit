using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Turns the toolkit's caller and the tenant a request names into a Tenancy caller: the caller's active seat
/// in that tenant, or nobody and why.
/// <para>
/// The request names the tenant by its slug only. The tenant's id and the seat's id always come from the
/// directory, found through the caller's verified identity, so nothing a caller sends picks a seat. The
/// toolkit's system caller is nobody here: system work in Tenancy is begun on purpose with
/// <see cref="TenancyWork"/>, never inherited from a request.
/// </para>
/// <para>
/// A host's middleware, which only makes the caller it is answered current, asks the same of
/// <see cref="ITenantSelection"/>, the very instance without its id types, and names no id.
/// </para>
/// <para>
/// Only a signed-in user whose token carries a seated role is looked up at all,
/// <see cref="TenantSelectionOptions.SeatedTokenRoles"/>: <c>authenticated</c> unless the application lists
/// others. A user with another token role is nobody in every tenant, and is told what a person without a seat
/// is told, so the answer says nothing about the seats their identity has. The list of a person's own seats,
/// <see cref="SeatsOfAsync"/>, follows the same rule.
/// </para>
/// </summary>
/// <param name="seats">Finds the caller's seats.</param>
/// <param name="options">Which token roles are seated; signed-in users with the role <c>authenticated</c> when left out.</param>
public sealed class TenantSelection<TTenantId, TSeatId>(ISeatDirectory<TTenantId, TSeatId> seats, TenantSelectionOptions? options = null) : ITenantSelection
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    private readonly TenantSelectionOptions _options = options ?? new TenantSelectionOptions();

    /// <summary>
    /// The Tenancy caller for <paramref name="caller"/> in the tenant <paramref name="tenantSlug"/> names.
    /// Every refusal comes back as nobody, with the refusal's code; nothing is thrown for a caller Tenancy
    /// does not let in.
    /// </summary>
    /// <param name="caller">Who is calling, as the toolkit says.</param>
    /// <param name="tenantSlug">The tenant the request names, by its slug; trimmed and lowercased here.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<TenancyCaller<TTenantId, TSeatId>> ResolveAsync(Caller caller, string? tenantSlug, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Kind != CallerKind.User || caller.UserId is not { } identity)
        {
            return TenancyCaller<TTenantId, TSeatId>.Nobody(TenancyRefusals.NotSeated);
        }

        // Before anything is looked up, and with the answer of a person without a seat: a token role that holds no
        // seat learns nothing about the tenants, not even that a slug is missing.
        if (!_options.Seats(caller.Role))
        {
            return TenancyCaller<TTenantId, TSeatId>.Nobody(TenancyRefusals.NotSeated);
        }

        var slug = tenantSlug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug))
        {
            return TenancyCaller<TTenantId, TSeatId>.Nobody(TenancyRefusals.TenantRequired);
        }

        // No such tenant and someone else's tenant answer the same, so a slug tells nobody whether it exists.
        var seat = await seats.FindAsync(identity, slug, cancellationToken).ConfigureAwait(false);
        if (seat is null)
        {
            return TenancyCaller<TTenantId, TSeatId>.Nobody(TenancyRefusals.NotSeated);
        }

        if (seat.TenantStatus != TenantStatus.Active)
        {
            return TenancyCaller<TTenantId, TSeatId>.Nobody(TenancyRefusals.TenantInactive);
        }

        return seat.SeatStatus != SeatStatus.Active
            ? TenancyCaller<TTenantId, TSeatId>.Nobody(TenancyRefusals.SeatSuspended)
            : TenancyCaller<TTenantId, TSeatId>.InSeat(seat.Tenant, seat.Seat);
    }

    /// <inheritdoc />
    async Task<ITenancyCaller> ITenantSelection.ResolveAsync(Caller caller, string? tenantSlug, CancellationToken cancellationToken)
        => await ResolveAsync(caller, tenantSlug, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Every seat <paramref name="caller"/> has, in every tenant and in any status, for a tenant picker: looked up
    /// by the verified identity of the caller's own token, and by nothing a request could supply. None for a
    /// caller that is no signed-in user, and none for a user whose token role holds no seat: nothing is looked up
    /// for them, and they are answered what a person without a seat is answered, as in
    /// <see cref="ResolveAsync"/>.
    /// </summary>
    /// <param name="caller">Who is calling, as the toolkit says.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<IReadOnlyList<SeatOfCaller<TTenantId, TSeatId>>> SeatsOfAsync(Caller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Kind != CallerKind.User || caller.UserId is not { } identity || !_options.Seats(caller.Role))
        {
            return [];
        }

        return await seats.AllOfAsync(identity, cancellationToken).ConfigureAwait(false);
    }
}
