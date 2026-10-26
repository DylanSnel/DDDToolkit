using System.Globalization;
using DDDToolkit.Exceptions;
using Examples.Tenancy.Shared.Application.Paging;
using GreenDonut.Data;

namespace Examples.Tenancy.Tenants.Application.History;

/// <summary>
/// What reading the access history refuses with that the Tenancy package has no code for, and the one place a
/// page of it is checked, for the seat's query and the operators' alike.
/// </summary>
/// <remarks>
/// A marker that is not the list's is refused under the package's own code, <c>tenancy.cursor-invalid</c>: its
/// text names no list. The translations are beside this class, in the two resource files of
/// <see cref="HistoryFailures"/>.
/// </remarks>
public static class HistoryRefusals
{
    /// <summary>A page was asked for with a size it cannot have. Arguments: <c>Max</c>, and <c>Field</c>, which is <c>size</c>.</summary>
    public const string PageSizeInvalid = "tenants.history.page-size-invalid";

    /// <summary>The English text of <see cref="PageSizeInvalid"/>, with its placeholder as written.</summary>
    public const string PageSizeInvalidText = "A page holds 1 to {Max} rows of the history.";

    /// <summary>A page was asked for by its first rows and by its last at once.</summary>
    public const string PageFromBothEnds = "tenants.history.page-from-both-ends";

    /// <summary>The English text of <see cref="PageFromBothEnds"/>.</summary>
    public const string PageFromBothEndsText = "A page is the first of a list or the last of it. Ask with first or with last, not with both.";

    /// <summary>How many rows a page holds when the caller does not say.</summary>
    public const int DefaultPage = 50;

    /// <summary>The most rows a page holds.</summary>
    public const int LargestPage = 200;

    /// <summary>Every code above: what a test holds the resource files of <see cref="HistoryFailures"/> to.</summary>
    public static IReadOnlyList<string> Codes { get; } = [PageSizeInvalid, PageFromBothEnds];

    /// <summary>
    /// <paramref name="paging"/> as a page of the history is read with: asked for from one end, with a size of 1
    /// to <see cref="LargestPage"/>, and <see cref="DefaultPage"/> when none was asked for. The read itself has
    /// no cap of its own and takes no page from both ends, so every query that pages the history comes through
    /// here first. The check is the one every paged query of the sample makes (<see cref="PageSizes"/>); what is
    /// said here is this list's sizes and its refusals.
    /// </summary>
    /// <param name="paging">The page a caller asked for.</param>
    /// <exception cref="RefusalException"><see cref="PageSizeInvalid"/>, or <see cref="PageFromBothEnds"/>.</exception>
    internal static PagingArguments Checked(PagingArguments paging)
        => PageSizes.Checked(
            paging,
            DefaultPage,
            LargestPage,
            sizeOutOfRange: () => new RefusalException(
                PageSizeInvalid,
                RefusalKind.Invalid,
                PageSizeInvalidText.Replace("{Max}", LargestPage.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Max"] = LargestPage, ["Field"] = "size" }),
            fromBothEnds: () => new RefusalException(PageFromBothEnds, RefusalKind.Invalid, PageFromBothEndsText));
}
