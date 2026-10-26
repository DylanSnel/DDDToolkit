using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A project was closed.</summary>
public sealed record ProjectClosed(ProjectId ProjectId) : DomainEvent;
