namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// When a membership, or a role held in one, counts: from <see cref="Starts"/>, and until <see cref="Ends"/>
/// when it has an end.
/// <para>
/// Not a toolkit value object, because it is stored as the two columns of the row that holds it rather than
/// as a value of its own. It is two moments and decides nothing by itself: whether an end may come before a
/// start is the member list's to refuse, under the codes of the resource the members belong to, before
/// anything changes.
/// </para>
/// </summary>
public readonly record struct MemberPeriod
{
    private MemberPeriod(DateTimeOffset starts, DateTimeOffset? ends)
    {
        Starts = starts;
        Ends = ends;
    }

    /// <summary>The first moment it counts.</summary>
    public DateTimeOffset Starts { get; }

    /// <summary>The first moment it no longer counts, or <see langword="null"/> when it has no end.</summary>
    public DateTimeOffset? Ends { get; }

    /// <summary>
    /// Whether the period covers no moment at all: it ends at or before it starts. The member list refuses
    /// such a period with the resource's <c>invalid-period</c> code.
    /// </summary>
    public bool IsEmpty => Ends is { } end && end <= Starts;

    /// <summary>A period from <paramref name="starts"/> with no end.</summary>
    /// <param name="starts">The first moment it counts.</param>
    public static MemberPeriod Open(DateTimeOffset starts) => new(starts, null);

    /// <summary>
    /// A period from <paramref name="starts"/> to <paramref name="ends"/>, or with no end when that is
    /// <see langword="null"/>: what a command that takes an optional end makes of it.
    /// </summary>
    /// <param name="starts">The first moment it counts.</param>
    /// <param name="ends">The first moment it no longer counts.</param>
    public static MemberPeriod Between(DateTimeOffset starts, DateTimeOffset? ends) => new(starts, ends);

    /// <summary>Whether the period covers <paramref name="moment"/>: it has started, and has not ended.</summary>
    /// <param name="moment">The moment to ask about, usually now.</param>
    public bool AppliesAt(DateTimeOffset moment) => Starts <= moment && (Ends is null || Ends > moment);
}
