namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;

/// <summary>Whether a project is still worked on.</summary>
public enum ProjectState
{
    /// <summary>Worked on: it can be renamed, moved and closed, and its crew changed.</summary>
    Open,

    /// <summary>Done with. Its crew still sees it; nothing about it changes until it is reopened.</summary>
    Closed,
}
