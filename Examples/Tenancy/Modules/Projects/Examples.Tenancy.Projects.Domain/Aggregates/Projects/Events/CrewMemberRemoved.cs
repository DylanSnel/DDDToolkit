using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A seat was taken off a project's crew, with every role it held there.</summary>
public sealed record CrewMemberRemoved(ProjectId ProjectId, SeatId SeatId) : DomainEvent;
