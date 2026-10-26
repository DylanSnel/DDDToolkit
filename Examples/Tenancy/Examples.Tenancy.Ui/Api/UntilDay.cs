namespace Examples.Tenancy.Ui.Api;

/// <summary>
/// A day picked as "until", as the instant the API reads for <c>until</c>.
/// </summary>
/// <remarks>
/// The API's end is the first moment something no longer counts. Somebody who picks Friday means Friday too, so
/// the instant sent is the start of the day after, in UTC: the membership or the role still counts all through the
/// day picked. Sent as the start of the day itself, Friday would be left out, and today would be refused as an end
/// that has passed already.
/// </remarks>
public static class UntilDay
{
    /// <summary>The first moment after <paramref name="day"/>, in UTC; <see langword="null"/> for no day, which is no end.</summary>
    public static DateTimeOffset? Instant(DateOnly? day)
        => day is { } picked ? new DateTimeOffset(picked.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    /// <summary>
    /// An end as a page shows it: the day that was picked, when the end is the first moment after a day, and the
    /// moment itself otherwise, for an end somebody set to the minute through the API. So whoever picked the first
    /// of December reads the first of December, and not the moment after it.
    /// </summary>
    /// <param name="end">The first moment something no longer counts.</param>
    public static string Shown(DateTimeOffset end)
    {
        var moment = end.ToUniversalTime();
        return moment.TimeOfDay == TimeSpan.Zero
            ? DateOnly.FromDateTime(moment.UtcDateTime).AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : moment.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
