using Examples.Tenancy.Tenants.Application.History;
using GreenDonut.Data;

namespace Examples.Tenancy.Tenants.Api.Rest;

/// <summary>
/// A page of the access history as a route asks for it and writes it. Two features' routes answer one: a tenant's
/// own history, and the operators' reading of any tenant's. So it is no feature's own, and sits beside the
/// features and not in one of them.
/// </summary>
internal static class HistoryPages
{
    /// <summary>The page a route was asked for, as the query takes it: so many rows, after a marker or from the newest.</summary>
    public static PagingArguments Asked(string? after, int? size) => new(first: size ?? HistoryRefusals.DefaultPage, after: after);

    /// <summary>
    /// A page of the history as every answer of this project writes it: the rows, each with who acted as a kind and,
    /// for a seat, its id, and the marker of the page after it. The event itself is given as JSON, as the query answers it.
    /// </summary>
    public static object Describe(Page<AccessHistoryEntry> page) => new
    {
        items = page.Items.Select(row => new
        {
            row.Id,
            @event = row.Event,
            row.OccurredAt,
            by = new { kind = row.ByKind, seatId = row.BySeat },
            details = System.Text.Json.Nodes.JsonNode.Parse(row.Details),
        }),
        // The marker of the page after this one is made from its last row, by the paging library itself.
        next = page.HasNextPage && page.Entries.Length > 0 ? page.CreateCursor(page.Entries[^1]) : null,
    };
}
