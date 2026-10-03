namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// One page of the projects the caller may see (<c>GET /projects</c>): the projects, the marker to ask the next
/// page with, which is <see langword="null"/> on the last, and the size of the whole list when it was asked for.
/// </summary>
public sealed record ProjectPageInfo(IReadOnlyList<ProjectInfo> Items, string? Next, int? Total = null);
