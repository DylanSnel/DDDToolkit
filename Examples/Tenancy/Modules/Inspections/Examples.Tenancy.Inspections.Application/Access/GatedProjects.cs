namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// What Projects' gate answered about the projects of a request about several, kept for that request's handler
/// as <see cref="GatedProject"/> is for a request about one: an answer for each project the caller may see, and
/// none for the others.
/// </summary>
/// <param name="Answers">What the gate answered, by project: only the projects the caller may see.</param>
public sealed record GatedProjects(IReadOnlyDictionary<ProjectId, ProjectAnswer> Answers);
