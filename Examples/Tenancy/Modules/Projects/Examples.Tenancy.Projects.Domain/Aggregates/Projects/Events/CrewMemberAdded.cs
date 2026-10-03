using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A seat was put on a project's crew. The roles it holds there are given one by one, each with an event of its own.</summary>
public sealed record CrewMemberAdded(ProjectId ProjectId, SeatId SeatId) : DomainEvent;
