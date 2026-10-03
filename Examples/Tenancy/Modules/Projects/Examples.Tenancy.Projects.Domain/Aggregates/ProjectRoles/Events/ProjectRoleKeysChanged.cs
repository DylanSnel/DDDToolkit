using DDDToolkit.BaseTypes;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.Events;

/// <summary>A project role gives other keys from now on, on every crew it is held on.</summary>
public sealed record ProjectRoleKeysChanged(ProjectRoleId ProjectRoleId) : DomainEvent;
