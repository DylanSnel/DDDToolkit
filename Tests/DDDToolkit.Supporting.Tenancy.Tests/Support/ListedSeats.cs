namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>A seat directory over a list, which records the lookups it was asked for.</summary>
public sealed class ListedSeats : ISeatDirectory<TenantId, SeatId>
{
    private readonly List<(Guid Identity, SeatOfCaller<TenantId, SeatId> Seat, object? Own)> _seats = [];

    /// <summary>Every lookup, in order: the identity and the slug as they reached the directory.</summary>
    public List<(Guid Identity, string Slug)> Lookups { get; } = [];

    /// <summary>Every identity whose seats in every tenant were asked for, in order.</summary>
    public List<Guid> Listings { get; } = [];

    /// <summary>Gives <paramref name="identity"/> a seat, and the application's own seat for it when there is one.</summary>
    public ListedSeats With(
        Guid identity,
        TenantId tenant,
        string slug,
        SeatId seat,
        TenantStatus tenantState = TenantStatus.Active,
        SeatStatus seatState = SeatStatus.Active,
        object? own = null)
    {
        _seats.Add((identity, new SeatOfCaller<TenantId, SeatId>(tenant, slug, "Tenant " + slug, tenantState, seat, seatState), own));
        return this;
    }

    /// <inheritdoc />
    public Task<SeatOfCaller<TenantId, SeatId>?> FindAsync(Guid identity, string tenantSlug, CancellationToken cancellationToken)
    {
        Lookups.Add((identity, tenantSlug));
        var found = _seats.FirstOrDefault(entry => entry.Identity == identity && entry.Seat.Slug == tenantSlug).Seat;
        return Task.FromResult<SeatOfCaller<TenantId, SeatId>?>(found);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SeatOfCaller<TenantId, SeatId>>> AllOfAsync(Guid identity, CancellationToken cancellationToken)
    {
        Listings.Add(identity);
        IReadOnlyList<SeatOfCaller<TenantId, SeatId>> seats = [.. _seats.Where(entry => entry.Identity == identity).Select(entry => entry.Seat)];
        return Task.FromResult(seats);
    }

    /// <inheritdoc />
    /// <remarks>Hands the view the application's own seat each seat was listed with.</remarks>
    public Task<IReadOnlyList<TView>> AllOfAsync<TSeat, TView>(Guid identity, Func<SeatOfCaller<TenantId, SeatId>, TSeat, TView> view, CancellationToken cancellationToken)
        where TSeat : class
    {
        Listings.Add(identity);
        IReadOnlyList<TView> seats =
        [
            .. _seats
                .Where(entry => entry.Identity == identity)
                .Select(entry => view(entry.Seat, entry.Own as TSeat ?? throw new InvalidOperationException($"Seat {entry.Seat.Seat} was listed with no {typeof(TSeat).Name}."))),
        ];
        return Task.FromResult(seats);
    }
}
