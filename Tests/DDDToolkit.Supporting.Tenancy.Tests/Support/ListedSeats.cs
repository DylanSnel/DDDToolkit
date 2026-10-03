namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>A seat directory over a list, which records the lookups it was asked for.</summary>
public sealed class ListedSeats : ISeatDirectory<TenantId, SeatId>
{
    private readonly List<(Guid Identity, SeatOfCaller<TenantId, SeatId> Seat)> _seats = [];

    /// <summary>Every lookup, in order: the identity and the slug as they reached the directory.</summary>
    public List<(Guid Identity, string Slug)> Lookups { get; } = [];

    /// <summary>Every identity whose seats in every tenant were asked for, in order.</summary>
    public List<Guid> Listings { get; } = [];

    /// <summary>Gives <paramref name="identity"/> a seat.</summary>
    public ListedSeats With(
        Guid identity,
        TenantId tenant,
        string slug,
        SeatId seat,
        TenantStatus tenantState = TenantStatus.Active,
        SeatStatus seatState = SeatStatus.Active)
    {
        _seats.Add((identity, new SeatOfCaller<TenantId, SeatId>(tenant, slug, "Tenant " + slug, tenantState, seat, "Seat", seatState)));
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
}
