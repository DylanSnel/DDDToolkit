using DDDToolkit.BaseTypes;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.Events;

/// <summary>A tenant made a project role, of its own or one of the starter roles when the tenant was set up.</summary>
public sealed record ProjectRoleMade(ProjectRoleId ProjectRoleId, TenantId TenantId) : DomainEvent;
