using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A project now hangs at another unit, which changes who reaches it through the organization.</summary>
public sealed record ProjectMoved(ProjectId ProjectId, OrganizationUnitId From, OrganizationUnitId To) : DomainEvent;
