using DDDToolkit.BaseTypes;
using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;

namespace Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.Events;

/// <summary>An inspection was recorded on a project.</summary>
/// <remarks>
/// A domain event of Inspections carries ids only: what a reader needs to know happened, and where to look. The
/// outbox stores it under the module's name and the class's, in the same save as the inspection. No integration
/// event is published from it; a module that wanted one would get a contract of its own.
/// </remarks>
public sealed record InspectionRecorded(InspectionId InspectionId, TenantId TenantId, ProjectId ProjectId, SeatId RecordedBy) : DomainEvent;
