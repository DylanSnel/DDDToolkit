using Examples.Tenancy.Shared.Application.Paging;
using FluentAssertions;
using GreenDonut.Data;

namespace Examples.Tenancy.Tests.Shared.Paging;

/// <summary>
/// The one check every paged query of the sample makes of the page it is asked for: one end of the list, a size
/// within the list's own, and the list's default when the caller names neither end.
/// </summary>
public sealed class PageSizesTests
{
    private const int Default = 50;

    private const int Largest = 200;

    private static readonly InvalidOperationException OutOfRange = new("size");

    private static readonly InvalidOperationException BothEnds = new("both ends");

    [Fact]
    public void A_page_that_names_no_end_is_the_first_of_the_lists_default_size()
    {
        Checked(default).Should().Be(new PagingArguments(first: Default));
        Checked(new PagingArguments(after: "a-marker")).Should().Be(new PagingArguments(first: Default, after: "a-marker"), "what else was asked for stays");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Largest)]
    public void A_size_within_the_range_is_taken_as_it_is_from_either_end(int size)
    {
        Checked(new PagingArguments(first: size)).Should().Be(new PagingArguments(first: size));
        Checked(new PagingArguments(last: size, before: "a-marker")).Should().Be(new PagingArguments(last: size, before: "a-marker"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Largest + 1)]
    public void A_size_out_of_range_is_refused_from_either_end(int size)
    {
        FluentActions.Invoking(() => Checked(new PagingArguments(first: size))).Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(OutOfRange);
        FluentActions.Invoking(() => Checked(new PagingArguments(last: size))).Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(OutOfRange, "the last rows are held to the sizes as the first are");
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(Largest + 1, Largest + 1)]
    public void A_page_asked_for_from_both_ends_is_refused_as_that_whatever_its_sizes(int first, int last)
    {
        FluentActions.Invoking(() => Checked(new PagingArguments(first: first, last: last)))
            .Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(BothEnds, "with both ends named there is no one size to judge");
    }

    [Fact]
    public void A_list_without_a_default_refuses_a_page_that_names_no_size()
    {
        FluentActions.Invoking(() => PageSizes.Checked(default, defaultSize: null, Largest, () => OutOfRange, () => BothEnds))
            .Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(OutOfRange);
        PageSizes.Checked(new PagingArguments(first: 3), defaultSize: null, Largest, () => OutOfRange, () => BothEnds).Should().Be(new PagingArguments(first: 3));
    }

    private static PagingArguments Checked(PagingArguments paging) => PageSizes.Checked(paging, Default, Largest, () => OutOfRange, () => BothEnds);
}
