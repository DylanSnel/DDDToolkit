using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;

/// <summary>An inspection's id.</summary>
/// <remarks>
/// Declared in the module rather than in a contracts project: no other module stores one or asks about one. The
/// HTTP API answers it to the client that recorded the inspection, which is all it is for outside this module.
/// </remarks>
[EntityId<Guid>("INSP")]
public readonly partial record struct InspectionId;
