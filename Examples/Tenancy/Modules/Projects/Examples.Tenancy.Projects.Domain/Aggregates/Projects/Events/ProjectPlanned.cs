using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A project's planned range was set, changed or taken away.</summary>
public sealed record ProjectPlanned(ProjectId ProjectId) : DomainEvent;
