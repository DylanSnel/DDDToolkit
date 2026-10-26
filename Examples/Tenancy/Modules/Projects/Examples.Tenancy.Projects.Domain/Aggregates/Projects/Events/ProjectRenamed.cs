using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A project was renamed.</summary>
public sealed record ProjectRenamed(ProjectId ProjectId) : DomainEvent;
