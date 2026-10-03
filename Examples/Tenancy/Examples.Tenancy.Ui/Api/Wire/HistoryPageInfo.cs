namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// One page of the tenant's access history, newest first, and the marker to ask the next page with,
/// <see langword="null"/> on the last (<c>GET /tenancy/history</c>).
/// </summary>
public sealed record HistoryPageInfo(IReadOnlyList<HistoryRowInfo> Items, string? Next = null);
