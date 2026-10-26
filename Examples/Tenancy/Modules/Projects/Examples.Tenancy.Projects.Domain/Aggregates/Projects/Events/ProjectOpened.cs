using DDDToolkit.BaseTypes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;

/// <summary>A project was opened at a unit, with its owner on the crew.</summary>
/// <remarks>
/// Like every domain event of Projects it carries ids only: what a reader needs to know changed, and where to
/// look. The outbox stores it under the module's name and the class's, <c>projects.project-opened</c>, in the same
/// save as the change. No integration event is published from Projects' events yet; a module that wanted one
/// would get a contract of its own, not these.
/// </remarks>
public sealed record ProjectOpened(ProjectId ProjectId, TenantId TenantId, OrganizationUnitId UnitId, SeatId OwnerSeatId) : DomainEvent;
