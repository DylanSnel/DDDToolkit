using System.Buffers;
using System.Buffers.Text;
using System.Text;
using GreenDonut.Data;
using GreenDonut.Data.Cursors;

namespace Examples.Tenancy.Shared.Infrastructure.Paging;

/// <summary>
/// Holds a paged read to the cursors of its own list: a marker that is not one the list could have given is
/// refused by the list's own code, before any statement runs.
/// </summary>
/// <remarks>
/// A list is paged by the paging library, which writes a cursor from what the list is ordered by and reads it
/// back with the same keys. Left to itself the library answers a marker it cannot read in three ways: a text that
/// is no cursor at all is read as the end of the list and answered an empty page, a cursor of a list with other
/// keys fails somewhere inside the read, and the head of a cursor can say how many pages to skip and what the
/// list's total is, which the library believes. A caller who sent any of those made a mistake, or tried
/// something, and is told so.
/// <para>
/// So before the page is read, the marker is read here by the library itself, with the keys of the query the
/// page is about to be read from, and what the library made of it is looked at: it read a value for every key the
/// list is ordered by, and it read no place among the pages unless the list gives such cursors. How a cursor is
/// written is the library's and is not read a second time here; only that the text is whole Base64 is checked
/// first, which the library does not hold itself to. Nothing is sent to the database to find any of it out, which
/// is why every failure of that reading can be answered as one refusal: nothing else runs inside it.
/// </para>
/// <para>
/// What this does not tell apart is what the library reads alike. A cursor of another list that is ordered by
/// keys of the same types is a place in this one too: a moment and an id are a place in every list ordered by a
/// moment and an id. And a text the library reads leniently, a value without the head in front of it, is read as
/// the place it names. A cursor is a place and never a right, so neither gives a caller anything it could not ask
/// for. A list ordered by a key that can be null would need more: the library reads a missing value and a null
/// alike, and here both are no cursor.
/// </para>
/// </remarks>
public static class ListCursors
{
    /// <summary>The longest marker read at all. A cursor holds the few values a list is ordered by.</summary>
    private const int LongestMarker = 512;

    /// <summary>
    /// The query itself, once the markers <paramref name="paging"/> carries are known to be cursors of it: put
    /// between the ordering and the paging of a read, so the page is read only with a cursor of its own list.
    /// </summary>
    /// <typeparam name="T">What the query answers.</typeparam>
    /// <param name="ordered">The query as it is about to be paged: filtered and ordered.</param>
    /// <param name="paging">What the page is asked with.</param>
    /// <param name="refusal">Makes the refusal of the list being paged, under its own code.</param>
    /// <exception cref="Exception">What <paramref name="refusal"/> makes, when a marker is not a cursor of this list.</exception>
    public static IQueryable<T> TakingOnlyItsOwnCursors<T>(this IQueryable<T> ordered, PagingArguments paging, Func<Exception> refusal)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(refusal);

        if (paging.After is null && paging.Before is null)
        {
            return ordered;
        }

        var keys = KeysOf(ordered);
        if ((paging.After is { } after && !Reads(after, keys, paging.EnableRelativeCursors))
            || (paging.Before is { } before && !Reads(before, keys, paging.EnableRelativeCursors)))
        {
            throw refusal();
        }

        return ordered;
    }

    /// <summary>
    /// Whether <paramref name="marker"/> is a cursor a page of <paramref name="ordered"/> could have given.
    /// </summary>
    /// <typeparam name="T">What the query answers.</typeparam>
    /// <param name="ordered">The query as it is paged: what it is ordered by is what its cursors are made of.</param>
    /// <param name="marker">The text a caller sent as a cursor.</param>
    /// <param name="relative">
    /// Whether the list gives cursors that carry a place among the pages. A cursor of a list that does not carries
    /// none, and one that does was written by somebody else.
    /// </param>
    public static bool IsACursorOf<T>(IQueryable<T> ordered, string marker, bool relative = false)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(marker);

        return Reads(marker, KeysOf(ordered), relative);
    }

    /// <summary>What a page of <paramref name="ordered"/> is ordered by, found the way the paging library finds it.</summary>
    private static CursorKey[] KeysOf<T>(IQueryable<T> ordered)
    {
        var parser = new CursorKeyParser();
        parser.Visit(ordered.Expression);
        return [.. parser.Keys];
    }

    private static bool Reads(string marker, CursorKey[] keys, bool relative)
    {
        if (keys.Length == 0 || marker.Length is 0 or > LongestMarker || !Ascii.IsValid(marker))
        {
            return false;
        }

        // A cursor is Base64. The library decodes what it can and reads the rest as the end; here what does not
        // decode whole is no cursor.
        Span<byte> bytes = stackalloc byte[LongestMarker];
        if (Base64.DecodeFromUtf8InPlace(bytes[..Encoding.ASCII.GetBytes(marker, bytes)], out _) != OperationStatus.Done)
        {
            return false;
        }

        Cursor cursor;
        try
        {
            // The library's own reading, with the keys of this list. Nothing but the marker is read in here, so
            // whatever it throws says the marker does not read.
            cursor = CursorParser.Parse(marker, keys);
        }
        catch (Exception)
        {
            return false;
        }

        // What the library made of it: a value for every key, where it leaves a key it found no value for empty,
        // and no place among the pages for a list that gives none.
        return cursor.Values.All(value => value is not null) && (relative || !cursor.IsRelative);
    }
}
