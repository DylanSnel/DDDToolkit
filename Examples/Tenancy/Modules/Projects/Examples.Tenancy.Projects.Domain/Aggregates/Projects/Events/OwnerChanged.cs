using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A project was given another owner; the old one stays on the crew, without the lead role.</summary>
public sealed record OwnerChanged(ProjectId ProjectId, SeatId From, SeatId To) : DomainEvent;
