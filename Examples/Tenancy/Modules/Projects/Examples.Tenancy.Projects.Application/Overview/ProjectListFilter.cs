namespace Examples.Tenancy.Projects.Application.Overview;

/// <summary>What of the projects a caller reaches a list keeps. Each part that is <see langword="null"/> keeps everything.</summary>
/// <param name="State">Only the projects in this state.</param>
/// <param name="Units">
/// Only the projects at one of these units: a set not yet read, over the rows of the reading it is handed to, so
/// it becomes a subquery of the list's statement. The application asks Tenancy for it, for a unit and everything
/// below it.
/// </param>
/// <param name="Text">
/// Only the projects whose number starts with this, or whose name contains it, whatever the case. The reading
/// matches it as text, never as a pattern.
/// </param>
public sealed record ProjectListFilter(ProjectState? State = null, IQueryable<OrganizationUnitId>? Units = null, string? Text = null)
{
    /// <summary>The filter that keeps every project.</summary>
    public static ProjectListFilter None { get; } = new();
}
