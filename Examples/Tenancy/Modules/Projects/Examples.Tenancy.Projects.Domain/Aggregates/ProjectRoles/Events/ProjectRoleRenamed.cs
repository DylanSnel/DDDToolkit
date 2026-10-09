using DDDToolkit.BaseTypes;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.Events;

/// <summary>A project role was given another name, or another description.</summary>
public sealed record ProjectRoleRenamed(ProjectRoleId ProjectRoleId) : DomainEvent;
