namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// One page of a project's inspections, newest first, whether the caller may record one, and the marker to ask the
/// next page with, <see langword="null"/> on the last (<c>GET /projects/{id}/inspections</c>).
/// </summary>
public sealed record InspectionsInfo(IReadOnlyList<InspectionInfo> Items, bool CanRecord, string? Next = null);
