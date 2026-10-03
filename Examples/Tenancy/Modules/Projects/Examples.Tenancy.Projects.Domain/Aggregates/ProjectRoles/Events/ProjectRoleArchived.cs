using DDDToolkit.BaseTypes;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.Events;

/// <summary>A project role was archived: it gives nothing from now on, and is given to nobody.</summary>
public sealed record ProjectRoleArchived(ProjectRoleId ProjectRoleId) : DomainEvent;
