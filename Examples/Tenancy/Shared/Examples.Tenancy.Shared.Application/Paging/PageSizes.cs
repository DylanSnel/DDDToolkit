using GreenDonut.Data;

namespace Examples.Tenancy.Shared.Application.Paging;

/// <summary>
/// Holds the page a paged query is asked for to what its list allows, before anything is read: one end of the
/// list, and a size within the list's own.
/// </summary>
/// <remarks>
/// A page is asked for forward, with <c>First</c> and <c>After</c>, or backward, with <c>Last</c> and
/// <c>Before</c>. The paging library reads as many rows as it is asked for, and throws for a page asked for from
/// both ends, so neither is its to refuse: each query holds its page here first, whichever way the request came
/// in, and answers with a refusal of its own module. Every paged query of the sample comes through this one
/// check, so the three lists refuse the same pages.
/// <para>
/// It knows no module: a query hands it its sizes and its refusals.
/// </para>
/// </remarks>
public static class PageSizes
{
    /// <summary>
    /// <paramref name="paging"/> as the list is read with: asked for from one end, with a size of 1 to
    /// <paramref name="largest"/>, and with <paramref name="defaultSize"/> from the start when it names neither
    /// end.
    /// </summary>
    /// <param name="paging">The page a caller asked for.</param>
    /// <param name="defaultSize">
    /// How many rows a page holds when the caller names no size, or <see langword="null"/> for a list whose
    /// callers always name one: no size is then refused as a size out of range.
    /// </param>
    /// <param name="largest">The most rows a page of the list holds.</param>
    /// <param name="sizeOutOfRange">Makes the list's refusal of a size that is none of its own.</param>
    /// <param name="fromBothEnds">Makes the list's refusal of a page asked for by its first rows and by its last at once.</param>
    /// <exception cref="Exception">What <paramref name="fromBothEnds"/> or <paramref name="sizeOutOfRange"/> made.</exception>
    public static PagingArguments Checked(PagingArguments paging, int? defaultSize, int largest, Func<Exception> sizeOutOfRange, Func<Exception> fromBothEnds)
    {
        ArgumentNullException.ThrowIfNull(sizeOutOfRange);
        ArgumentNullException.ThrowIfNull(fromBothEnds);

        // Both ends first: with both given there is no one size to judge.
        if (paging.First is not null && paging.Last is not null)
        {
            throw fromBothEnds();
        }

        // Each end on its own: a size that is given is held to the range, whichever end it is the size of.
        if (paging.First is < 1 || paging.First > largest || paging.Last is < 1 || paging.Last > largest)
        {
            throw sizeOutOfRange();
        }

        if (paging.First is not null || paging.Last is not null)
        {
            return paging;
        }

        return defaultSize is { } size ? paging with { First = size } : throw sizeOutOfRange();
    }
}
