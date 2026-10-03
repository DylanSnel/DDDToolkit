namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// When a grant applies: from <see cref="Starts"/>, and until <see cref="Ends"/> when it has an end.
/// <para>
/// Not a toolkit value object, because it is stored as the two columns of the row that holds it rather than
/// as a value of its own; it only makes sure the two agree when a period is made.
/// </para>
/// </summary>
public readonly record struct GrantPeriod
{
    private GrantPeriod(DateTimeOffset starts, DateTimeOffset? ends)
    {
        Starts = starts;
        Ends = ends;
    }

    /// <summary>The first moment the grant applies.</summary>
    public DateTimeOffset Starts { get; }

    /// <summary>The first moment it no longer applies, or <see langword="null"/> when it has no end.</summary>
    public DateTimeOffset? Ends { get; }

    /// <summary>A period from <paramref name="starts"/> with no end.</summary>
    /// <param name="starts">The first moment the grant applies.</param>
    public static GrantPeriod Open(DateTimeOffset starts) => new(starts, null);

    /// <summary>A period from <paramref name="starts"/> to <paramref name="ends"/>, or with no end when that is <see langword="null"/>.</summary>
    /// <param name="starts">The first moment the grant applies.</param>
    /// <param name="ends">The first moment it no longer applies.</param>
    /// <exception cref="DDDToolkit.Exceptions.RefusalException"><c>tenancy.invalid-period</c>: it ends at or before it starts.</exception>
    public static GrantPeriod Between(DateTimeOffset starts, DateTimeOffset? ends)
        => ends is { } end && end <= starts
            ? throw TenancyRefusals.Of(TenancyRefusals.InvalidPeriod)
            : new GrantPeriod(starts, ends);

    /// <summary>Whether the period covers <paramref name="moment"/>: it has started, and has not ended.</summary>
    /// <param name="moment">The moment to ask about, usually now.</param>
    public bool AppliesAt(DateTimeOffset moment) => Starts <= moment && (Ends is null || Ends > moment);
}
