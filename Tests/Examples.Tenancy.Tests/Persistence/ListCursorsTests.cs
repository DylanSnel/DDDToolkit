using Examples.Tenancy.Shared.Infrastructure.Paging;
using FluentAssertions;
using GreenDonut.Data;
using GreenDonut.Data.Cursors;

namespace Examples.Tenancy.Tests.Persistence;

/// <summary>
/// The check every paged read of the sample makes before it pages (<see cref="ListCursors"/>), asked without a
/// database: what the paging library writes for a list is that list's cursor, and a text that is no cursor, a
/// cursor of a list with other keys and a cursor with a head somebody wrote are not.
/// </summary>
public sealed class ListCursorsTests
{
    /// <summary>A list ordered by one text, as the projects are by their number.</summary>
    private static readonly IQueryable<Numbered> ByNumber = Array.Empty<Numbered>().AsQueryable().OrderBy(row => row.Number);

    /// <summary>A list ordered by a moment and then an id, as the inspections and the access history are.</summary>
    private static readonly IQueryable<Dated> ByMoment = Array.Empty<Dated>().AsQueryable().OrderByDescending(row => row.At).ThenBy(row => row.Id);

    private static readonly Dated SomeRow = new(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero), new Guid("0a000000-0000-4000-8000-000000000001"));

    public static TheoryData<string, string> NoCursors => new()
    {
        { "plain text", Markers.PlainText },
        { "nothing", string.Empty },
        { "a head and no value", Markers.HeadAlone },
        { "a head that does not close", Markers.From("{P-001") },
        { "a head that is not three numbers", Markers.UnreadableHead },
        { "a head that says where the list is", Markers.From("{3|0|99}P-001") },
        { "two values for one key", Markers.From("{}P-001:P-002") },
        { "Base64 cut short", Markers.From("{}P-001")[..^3] },
        { "a letter that is not ASCII", "e31QLTAwMQ" + (char)0xE9 + "=" },
        { "a text longer than any cursor", Markers.From("{}" + new string('P', 600)) },
    };

    [Fact]
    public void What_the_library_writes_for_a_list_is_a_cursor_of_that_list()
    {
        var number = CursorOf(ByNumber, new Numbered("P-001"));
        var moment = CursorOf(ByMoment, SomeRow);

        number.Should().Be(Markers.From("{}P-001"), "the head is empty and the value follows it");
        ListCursors.IsACursorOf(ByNumber, number).Should().BeTrue();
        ListCursors.IsACursorOf(ByMoment, moment).Should().BeTrue();

        // A colon inside a value is written with a backslash before it, and is part of that value.
        ListCursors.IsACursorOf(ByNumber, CursorOf(ByNumber, new Numbered("P:001"))).Should().BeTrue();
    }

    [Fact]
    public void A_cursor_of_a_list_with_other_keys_is_not_a_cursor_of_this_one()
    {
        ListCursors.IsACursorOf(ByMoment, CursorOf(ByNumber, new Numbered("P-001"))).Should().BeFalse("one value where the list is ordered by two");
        ListCursors.IsACursorOf(ByNumber, CursorOf(ByMoment, SomeRow)).Should().BeFalse("two values where the list is ordered by one");
        ListCursors.IsACursorOf(ByMoment, Markers.From("{}P-001:P-002")).Should().BeFalse("two values, neither a moment nor an id");
        ListCursors.IsACursorOf(ByMoment, CursorOf(ByMoment, SomeRow)[..^4]).Should().BeFalse("the id is cut short");
    }

    [Theory]
    [MemberData(nameof(NoCursors))]
    public void A_text_that_is_no_cursor_is_not_read_as_a_place(string what, string marker)
        => ListCursors.IsACursorOf(ByNumber, marker).Should().BeFalse("{0} is no cursor", what);

    [Fact]
    public void A_head_that_says_where_the_list_is_counts_only_for_a_list_that_writes_one()
    {
        var crafted = Markers.WithHead(CursorOf(ByNumber, new Numbered("P-001")), "3|0|99");

        ListCursors.IsACursorOf(ByNumber, crafted).Should().BeFalse("the lists of the sample write an empty head");
        ListCursors.IsACursorOf(ByNumber, crafted, relative: true).Should().BeTrue("a list with relative cursors writes three numbers");
        ListCursors.IsACursorOf(ByNumber, Markers.From("{3|0}P-001"), relative: true).Should().BeFalse("and there are three");
    }

    [Fact]
    public void A_read_is_refused_with_the_lists_own_refusal_and_is_left_alone_otherwise()
    {
        var own = CursorOf(ByNumber, new Numbered("P-001"));
        Func<Exception> refusal = () => new InvalidOperationException("the list's own");

        ByNumber.TakingOnlyItsOwnCursors(new PagingArguments(first: 2), refusal).Should().BeSameAs(ByNumber, "a first page carries no marker");
        ByNumber.TakingOnlyItsOwnCursors(new PagingArguments(first: 2, after: own), refusal).Should().BeSameAs(ByNumber);
        ByNumber.TakingOnlyItsOwnCursors(new PagingArguments(last: 2, before: own), refusal).Should().BeSameAs(ByNumber);

        FluentActions.Invoking(() => ByNumber.TakingOnlyItsOwnCursors(new PagingArguments(first: 2, after: Markers.PlainText), refusal))
            .Should().Throw<InvalidOperationException>().WithMessage("the list's own");
        FluentActions.Invoking(() => ByNumber.TakingOnlyItsOwnCursors(new PagingArguments(last: 2, before: Markers.HeadAlone), refusal))
            .Should().Throw<InvalidOperationException>("a marker to page backward from is held to the same");
    }

    /// <summary>
    /// The check is the library's own reading of a marker, and what the library made of it. It does not read how
    /// a cursor is written a second time, so a text the library reads leniently is the place the library reads:
    /// pinned here, so that a change of the library's reading shows.
    /// </summary>
    [Fact]
    public void A_text_the_library_reads_leniently_is_the_place_it_names()
    {
        ListCursors.IsACursorOf(ByNumber, Markers.From("P-001")).Should().BeTrue("a value with no head in front of it is read as that value");
        ListCursors.IsACursorOf(ByNumber, Markers.From("{}P-001:")).Should().BeTrue("a separator after the last value is read as its end");
        ListCursors.IsACursorOf(ByNumber, Markers.From("{3x|0|99}P-001"), relative: true).Should().BeTrue("a number in the head is read as far as it is one");

        // None of them is a way past what the check is for: a head that says where the list is stays refused for
        // a list that writes none, however it is spelled.
        ListCursors.IsACursorOf(ByNumber, Markers.From("{3x|0|99}P-001")).Should().BeFalse();
    }

    /// <summary>
    /// What the check cannot tell apart, said here so nobody takes it for more: two lists ordered by keys of the
    /// same types give cursors of one shape, and a place in the one reads as a place in the other.
    /// </summary>
    [Fact]
    public void A_cursor_of_another_list_ordered_by_keys_of_the_same_types_reads_as_a_place()
    {
        var elsewhere = Array.Empty<Dated>().AsQueryable().OrderByDescending(row => row.At).ThenByDescending(row => row.Id);

        ListCursors.IsACursorOf(ByMoment, CursorOf(elsewhere, SomeRow)).Should().BeTrue("a moment and an id are a place in every list ordered by a moment and an id");
    }

    /// <summary>The cursor the paging library writes for <paramref name="row"/> of <paramref name="list"/>.</summary>
    private static string CursorOf<T>(IQueryable<T> list, T row)
    {
        var keys = new CursorKeyParser();
        keys.Visit(list.Expression);
        return CursorFormatter.Format(row, [.. keys.Keys], default);
    }

    private sealed record Numbered(string Number);

    private sealed record Dated(DateTimeOffset At, Guid Id);
}
