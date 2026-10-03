using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Validation;

namespace Examples.Tenancy.Shared.Domain.ValueObjects;

/// <summary>
/// A range of calendar days, from a first day to a last, both counted in. A value object: two ranges with the
/// same days are the same range.
/// </summary>
/// <remarks>
/// Days, not moments: a project is planned for days, and an inspection is about days, whatever the clock said
/// where somebody stood. So it is not the Tenancy package's <c>GrantPeriod</c>, which is about the moments access
/// counts for and may have no end. A range always has both days, and the last is never before the first; one
/// day is a range from that day to itself.
/// <para>
/// It lives in the shared domain project because two modules use it and neither owns it. Projects keeps a
/// project's planned range in one, Inspections the days an inspection covers, and the rule between them, that an
/// inspection's days lie within the project's planned range, compares the two with <see cref="Contains(DateRange)"/>.
/// </para>
/// <para>
/// A range that was only constructed is not yet known to hold: <c>IsValid</c> says, and an aggregate that is
/// handed one refuses a range that does not, with a code of its own.
/// </para>
/// </remarks>
/// <param name="From">The first day.</param>
/// <param name="Until">The last day, counted in.</param>
[ValueObject]
public partial record DateRange(DateOnly From, DateOnly Until)
{
    /// <summary>The range of one day.</summary>
    public static DateRange Of(DateOnly day) => new(day, day);

    /// <summary>
    /// The range two optional days say, as a request or a row carries them: none when both are left out, and a
    /// range that does not hold when only one is given, so whoever is handed it refuses it by its own code.
    /// </summary>
    /// <param name="from">The first day, or <see langword="null"/>.</param>
    /// <param name="until">The last day, or <see langword="null"/>.</param>
    public static DateRange? Of(DateOnly? from, DateOnly? until)
        => from is null && until is null ? null : new DateRange(from ?? DateOnly.MaxValue, until ?? DateOnly.MinValue);

    /// <summary>Whether <paramref name="day"/> is one of the range's days.</summary>
    public bool Contains(DateOnly day) => From <= day && day <= Until;

    /// <summary>Whether every day of <paramref name="other"/> is one of the range's days.</summary>
    public bool Contains(DateRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return From <= other.From && other.Until <= Until;
    }

    /// <summary>Whether the range and <paramref name="other"/> have at least one day in common.</summary>
    public bool Overlaps(DateRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return From <= other.Until && other.From <= Until;
    }

    /// <summary>The two days as ISO dates, such as <c>2026-10-01 to 2026-10-31</c>.</summary>
    public override string ToString() => $"{From:yyyy-MM-dd} to {Until:yyyy-MM-dd}";

    /// <inheritdoc />
    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (Until < From)
        {
            errors.Add("A range's last day is not before its first.", nameof(Until), "BeforeFrom", Until);
        }
    }
}
