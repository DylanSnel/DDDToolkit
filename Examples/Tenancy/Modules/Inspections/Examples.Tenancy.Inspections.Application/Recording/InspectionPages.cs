using Examples.Tenancy.Inspections.Application.Recording.Queries;
using Examples.Tenancy.Shared.Application.Paging;
using GreenDonut.Data;

namespace Examples.Tenancy.Inspections.Application.Recording;

/// <summary>Holds a page of inspections to what the list allows, for every query that pages them.</summary>
/// <remarks>
/// HotChocolate's paging reads as many rows as it is asked for, and throws for a page asked for from both ends,
/// so both are the module's to refuse: a handler holds the page here before anything is read, whichever way the
/// request came in. The check itself is the one every paged query of the sample makes
/// (<see cref="PageSizes"/>); what is said here is this list's sizes and its refusals.
/// </remarks>
internal static class InspectionPages
{
    /// <summary>
    /// <paramref name="paging"/> as a page of inspections is read with: asked for from one end, with a size of 1
    /// to <see cref="ProjectInspections.LargestPage"/>.
    /// </summary>
    /// <param name="paging">What the request asked for.</param>
    /// <param name="defaultSize">How many a page holds when the request names no size, or <see langword="null"/> where a size is always named.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>inspections.page-size-invalid</c>, with <c>Max</c>; <c>inspections.page-from-both-ends</c> for a page
    /// asked for by its first rows and by its last at once.
    /// </exception>
    public static PagingArguments Checked(PagingArguments paging, int? defaultSize = null)
        => PageSizes.Checked(
            paging,
            defaultSize,
            ProjectInspections.LargestPage,
            sizeOutOfRange: () => InspectionRefusals.Refuse(InspectionRefusals.PageSizeInvalid, ("Max", ProjectInspections.LargestPage)),
            fromBothEnds: () => InspectionRefusals.Refuse(InspectionRefusals.PageFromBothEnds));
}
