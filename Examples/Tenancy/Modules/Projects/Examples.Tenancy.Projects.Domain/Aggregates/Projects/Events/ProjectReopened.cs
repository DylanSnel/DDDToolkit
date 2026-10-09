using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A closed project was reopened, and can be worked on again.</summary>
public sealed record ProjectReopened(ProjectId ProjectId) : DomainEvent;
