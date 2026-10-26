using DDDToolkit.BaseTypes;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A crew member was given a project role on the project.</summary>
public sealed record CrewRoleGiven(ProjectId ProjectId, SeatId SeatId, ProjectRoleId RoleId) : DomainEvent;
