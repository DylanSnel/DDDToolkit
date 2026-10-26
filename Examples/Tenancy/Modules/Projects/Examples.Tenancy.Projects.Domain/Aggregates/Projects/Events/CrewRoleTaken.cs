using DDDToolkit.BaseTypes;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A project role was taken from a crew member, who stays on the crew.</summary>
public sealed record CrewRoleTaken(ProjectId ProjectId, SeatId SeatId, ProjectRoleId RoleId) : DomainEvent;
