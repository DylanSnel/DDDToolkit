using DDDToolkit.Exceptions;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Shared.ValueObjects;

/// <summary>
/// The range of calendar days two modules share, on its own: which days it counts in, when it holds, and how two
/// ranges lie to each other. What Projects and Inspections do with it is in their scenarios.
/// </summary>
public sealed class DateRangeTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    /// <summary>Monday to Sunday.</summary>
    private static DateRange Week => new(Monday, Monday.AddDays(6));

    [Fact]
    public void A_range_counts_its_first_and_its_last_day_in()
    {
        Week.Contains(Monday).Should().BeTrue();
        Week.Contains(Monday.AddDays(6)).Should().BeTrue("the last day is one of its days");
        Week.Contains(Monday.AddDays(-1)).Should().BeFalse();
        Week.Contains(Monday.AddDays(7)).Should().BeFalse();
    }

    [Fact]
    public void A_range_holds_when_its_last_day_is_not_before_its_first()
    {
        DateRange.Of(Monday).IsValid.Should().BeTrue("one day is a range from that day to itself");
        Week.IsValid.Should().BeTrue();

        var backwards = new DateRange(Monday, Monday.AddDays(-1));
        backwards.IsValid.Should().BeFalse();
        backwards.ValidationErrors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(DateRange.Until));
        backwards.Invoking(range => range.EnsureValidated()).Should().Throw<InvalidValueObjectException>();
    }

    [Fact]
    public void A_range_contains_a_range_whose_days_are_all_its_own()
    {
        Week.Contains(Week).Should().BeTrue("a range lies within itself");
        Week.Contains(new DateRange(Monday.AddDays(2), Monday.AddDays(3))).Should().BeTrue();
        Week.Contains(new DateRange(Monday.AddDays(5), Monday.AddDays(7))).Should().BeFalse("one day past the end is outside");
        Week.Contains(new DateRange(Monday.AddDays(-1), Monday)).Should().BeFalse("one day before the start is outside");
        DateRange.Of(Monday).Contains(Week).Should().BeFalse();
    }

    [Fact]
    public void Two_ranges_overlap_when_they_have_a_day_in_common()
    {
        var next = new DateRange(Monday.AddDays(7), Monday.AddDays(13));
        var around = new DateRange(Monday.AddDays(6), Monday.AddDays(8));

        Week.Overlaps(next).Should().BeFalse("the day after the last is no day of the range");
        next.Overlaps(Week).Should().BeFalse();
        Week.Overlaps(around).Should().BeTrue("they share the Sunday");
        around.Overlaps(Week).Should().BeTrue();
        around.Overlaps(next).Should().BeTrue();
        Week.Overlaps(DateRange.Of(Monday.AddDays(3))).Should().BeTrue("a range within another overlaps it");
    }

    [Fact]
    public void Two_ranges_of_the_same_days_are_the_same_range()
    {
        new DateRange(Monday, Monday.AddDays(6)).Should().Be(Week);
        new DateRange(Monday, Monday.AddDays(5)).Should().NotBe(Week);
        Week.ToString().Should().Be("2026-10-05 to 2026-10-11");
    }

    [Fact]
    public void Two_optional_days_say_a_range_none_or_one_that_does_not_hold()
    {
        DateRange.Of(Monday, Monday.AddDays(6)).Should().Be(Week);
        DateRange.Of(null, null).Should().BeNull("both left out is no range");
        DateRange.Of(Monday, null)!.IsValid.Should().BeFalse("half a range is not a range, and whoever is handed it refuses it");
        DateRange.Of(null, Monday)!.IsValid.Should().BeFalse();
    }
}
