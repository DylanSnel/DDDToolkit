namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>A project role a crew member holds on the project, by its id, with the period it holds it for.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="StartsAt">When the role starts to count.</param>
/// <param name="EndsAt">When it stops, or <see langword="null"/>.</param>
/// <param name="AppliesNow">Whether it counts now: its own period applies, and so does the membership it is held in.</param>
public sealed record CrewRoleOverview(ProjectRoleId RoleId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AppliesNow);
