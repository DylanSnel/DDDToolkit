namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>A role a crew member holds, as a reading returns it.</summary>
/// <param name="RoleId">The project role.</param>
/// <param name="StartsAt">The first moment the role counts.</param>
/// <param name="EndsAt">The first moment it no longer counts, or <see langword="null"/> when it has no end.</param>
public sealed record CrewRoleData(ProjectRoleId RoleId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt);
